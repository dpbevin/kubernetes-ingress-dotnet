using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.ReverseProxy.Abstractions;
using Microsoft.ReverseProxy.Service;
using Microsoft.ReverseProxy.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bevo.ReverseProxy.Kube.Discovery
{
    public class KubernetesConfigProvider : IProxyConfigProvider, IAsyncDisposable
    {
        private readonly object _lockObject = new object();
        private readonly TaskCompletionSource<int> _initalConfigLoadTcs = new TaskCompletionSource<int>();
        private readonly ILogger<KubernetesConfigProvider> _logger;
        private readonly IClock _clock;
        private readonly IKubernetesDiscoverer _discoverer;

        private volatile ConfigurationSnapshot _snapshot;
        private CancellationTokenSource _changeToken;
        private bool _disposed;

        private readonly CancellationTokenSource _backgroundCts;
        private readonly Task _backgroundTask;

        public KubernetesConfigProvider(
            ILogger<KubernetesConfigProvider> logger,
            IClock clock,
            IKubernetesDiscoverer discoverer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _discoverer = discoverer ?? throw new ArgumentNullException(nameof(discoverer));

            _backgroundCts = new CancellationTokenSource();
            _backgroundTask = KubernetesDiscoveryLoop();
        }

        public IProxyConfig GetConfig()
        {
            if (_snapshot != null)
            {
                return _snapshot;
            }

            WaitForDiscoveryOrCreateEmptyConfig();
            Debug.Assert(_snapshot != null);
            return _snapshot;

            void WaitForDiscoveryOrCreateEmptyConfig()
            {
                //if (_optionsMonitor.CurrentValue.AllowStartBeforeDiscovery)
                if (true)
                {
                    lock (_lockObject)
                    {
                        if (_snapshot == null)
                        {
                            Log.StartWithoutInitialServiceFabricDiscovery(_logger);
                            UpdateSnapshot(new List<ProxyRoute>(), new List<Cluster>());
                        }
                    }
                }
                else
                {
                    // NOTE: The callstack up to this point is already synchronously blocking.
                    // There isn't much we can do to avoid this blocking wait on startup.
                    Log.WaitingForInitialServiceFabricDiscovery(_logger);
                    _initalConfigLoadTcs.Task.Wait();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;

                _changeToken?.Dispose();

                // Stop discovery loop...
                _backgroundCts.Cancel();
                await _backgroundTask;
                _backgroundCts.Dispose();
            }
        }

        private async Task KubernetesDiscoveryLoop()
        {
            Log.StartingServiceFabricDiscoveryLoop(_logger);
            var first = true;
            var cancellation = _backgroundCts.Token;
            while (true)
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (!first)
                    {
                        //await _clock.Delay(_optionsMonitor.CurrentValue.DiscoveryPeriod, cancellation);
                        await _clock.Delay(10000, cancellation);
                    }

                    var result = await _discoverer.DiscoverAsync(cancellation);
                    UpdateSnapshot(result.Routes, result.Clusters);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // Graceful shutdown
                    Log.ServiceFabricDiscoveryLoopEndedGracefully(_logger);
                    return;
                }
                catch (Exception ex)
                {
                    Log.ServiceFabricDiscoveryLoopFailed(_logger, ex);
                }

                first = false;
            }
        }

        private void UpdateSnapshot(IReadOnlyList<ProxyRoute> routes, IReadOnlyList<Cluster> clusters)
        {
            // Prevent overlapping updates
            lock (_lockObject)
            {
                using var oldToken = _changeToken;
                _changeToken = new CancellationTokenSource();
                _snapshot = new ConfigurationSnapshot()
                {
                    Routes = routes,
                    Clusters = clusters,
                    ChangeToken = new CancellationChangeToken(_changeToken.Token)
                };

                try
                {
                    oldToken?.Cancel(throwOnFirstException: false);
                }
                catch (Exception ex)
                {
                    Log.ErrorSignalingChange(_logger, ex);
                }
            }

            _initalConfigLoadTcs.TrySetResult(0);
        }

        // TODO: Perhaps YARP should provide this type?
        private sealed class ConfigurationSnapshot : IProxyConfig
        {
            public IReadOnlyList<ProxyRoute> Routes { get; internal set; }

            public IReadOnlyList<Cluster> Clusters { get; internal set; }

            public IChangeToken ChangeToken { get; internal set; }
        }

        private static class Log
        {
            private static readonly Action<ILogger, Exception> _errorSignalingChange =
                LoggerMessage.Define(
                    LogLevel.Error,
                    EventIds.ErrorSignalingChange,
                    "An exception was thrown from the change notification.");

            private static readonly Action<ILogger, Exception> _startWithoutInitialServiceFabricDiscovery =
                LoggerMessage.Define(
                    LogLevel.Information,
                    EventIds.StartWithoutInitialServiceFabricDiscovery,
                    $"Proceeding without initial Service Fabric discovery results due to AllowStartBeforeDiscovery = true."); // {nameof(_optionsMonitor.CurrentValue.AllowStartBeforeDiscovery)} = true.");

            private static readonly Action<ILogger, Exception> _waitingForInitialServiceFabricDiscovery =
                LoggerMessage.Define(
                    LogLevel.Information,
                    EventIds.WaitingForInitialServiceFabricDiscovery,
                    $"Waiting for initial Service Fabric discovery results due to AllowStartBeforeDiscovery = false."); //{nameof(_optionsMonitor.CurrentValue.AllowStartBeforeDiscovery)} = false.");

            private static readonly Action<ILogger, Exception> _startingServiceFabricDiscoveryLoop =
                LoggerMessage.Define(
                    LogLevel.Information,
                    EventIds.StartingServiceFabricDiscoveryLoop,
                    "Service Fabric discovery loop is starting");

            private static readonly Action<ILogger, Exception> _serviceFabricDiscoveryLoopEndedGracefully =
                LoggerMessage.Define(
                    LogLevel.Information,
                    EventIds.ServiceFabricDiscoveryLoopEndedGracefully,
                    "Service Fabric discovery loop is ending gracefully");

            private static readonly Action<ILogger, Exception> _serviceFabricDiscoveryLoopFailed =
                LoggerMessage.Define(
                    LogLevel.Error,
                    EventIds.ServiceFabricDiscoveryLoopFailed,
                    "Swallowing unhandled exception from Service Fabric loop...");

            public static void ErrorSignalingChange(ILogger logger, Exception exception)
            {
                _errorSignalingChange(logger, exception);
            }

            public static void StartWithoutInitialServiceFabricDiscovery(ILogger<KubernetesConfigProvider> logger)
            {
                _startWithoutInitialServiceFabricDiscovery(logger, null);
            }

            public static void WaitingForInitialServiceFabricDiscovery(ILogger<KubernetesConfigProvider> logger)
            {
                _waitingForInitialServiceFabricDiscovery(logger, null);
            }

            public static void StartingServiceFabricDiscoveryLoop(ILogger<KubernetesConfigProvider> logger)
            {
                _startingServiceFabricDiscoveryLoop(logger, null);
            }

            public static void ServiceFabricDiscoveryLoopEndedGracefully(ILogger<KubernetesConfigProvider> logger)
            {
                _serviceFabricDiscoveryLoopEndedGracefully(logger, null);
            }

            public static void ServiceFabricDiscoveryLoopFailed(ILogger<KubernetesConfigProvider> logger, Exception exception)
            {
                _serviceFabricDiscoveryLoopFailed(logger, exception);
            }
        }
    }
}
