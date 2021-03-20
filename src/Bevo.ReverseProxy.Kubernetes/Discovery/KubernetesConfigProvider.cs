// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.ReverseProxy.Abstractions;
using Microsoft.ReverseProxy.Service;

namespace Bevo.ReverseProxy.Kube
{
    public class KubernetesConfigProvider : IProxyConfigProvider, IAsyncDisposable
    {
        private readonly object _lockObject = new object();

        private readonly ILogger<KubernetesConfigProvider> _logger;

        private readonly CancellationTokenSource _backgroundCts;

        private readonly IIngressController _controller;

        private readonly Task _backgroundTask;

        private readonly Debouncer _debouncer;

        private readonly ManualResetEventSlim _changeSignal = new ManualResetEventSlim(true);   // Check for ingresses on startup.

        private volatile ConfigurationSnapshot _snapshot;

        private CancellationTokenSource _changeToken;

        private bool _disposed;

        private string runningConfigurationHash;


        public KubernetesConfigProvider(IIngressController controller, ILogger<KubernetesConfigProvider> logger)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _backgroundCts = new CancellationTokenSource();
            _backgroundTask = KubernetesDiscoveryLoop();

            _debouncer = new Debouncer(TimeSpan.FromMilliseconds(750));

            // Wait for changes from the store (but debounce).
            ChangeToken.OnChange<object>(
                () => _controller.ChangeToken,
                _ => _debouncer.Debounce(() =>
                {
                    _logger.LogInformation("Kube Changed");
                    _changeSignal.Set();
                }),
                null);
        }

        public IProxyConfig GetConfig()
        {
            if (_snapshot != null)
            {
                return _snapshot;
            }

            lock (_lockObject)
            {
                if (_snapshot == null)
                {
                    UpdateSnapshot(new List<ProxyRoute>(), new List<Cluster>());
                }
            }

            Debug.Assert(_snapshot != null);
            return _snapshot;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;

                _changeToken?.Dispose();
                _changeSignal?.Dispose();

                // Stop discovery loop...
                _backgroundCts.Cancel();
                await _backgroundTask;
                _backgroundCts.Dispose();
            }
        }

        private async Task KubernetesDiscoveryLoop()
        {
            Log.StartingKubernetesDiscoveryLoop(_logger);

            var cancellation = _backgroundCts.Token;

            // Initial delay
            await Task.Delay(500, cancellation);

            while (true)
            {
                try
                {
                    // Wait for changes to be detected
                    _changeSignal.Wait(cancellation);
                    _changeSignal.Reset();
                    cancellation.ThrowIfCancellationRequested();

                    var result = await _controller.GetConfiguration(cancellation);

                    if (runningConfigurationHash != result.ConfigurationHash)
                    {
                        UpdateSnapshot(result.Routes, result.Clusters);
                        runningConfigurationHash = result.ConfigurationHash;
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // Graceful shutdown
                    Log.KubernetesDiscoveryLoopEndedGracefully(_logger);
                    return;
                }
                catch (Exception ex)
                {
                    Log.KubernetesDiscoveryLoopFailed(_logger, ex);
                }
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

            private static readonly Action<ILogger, Exception> _startingKubernetesDiscoveryLoop =
                LoggerMessage.Define(
                    LogLevel.Information,
                    EventIds.StartingKubernetesDiscoveryLoop,
                    "Kubernetes discovery loop is starting");

            private static readonly Action<ILogger, Exception> _kubernetesDiscoveryLoopEndedGracefully =
                LoggerMessage.Define(
                    LogLevel.Information,
                    EventIds.DiscoveryLoopEndedGracefully,
                    "Kubernetes discovery loop is ending gracefully");

            private static readonly Action<ILogger, Exception> _kubernetesDiscoveryLoopFailed =
                LoggerMessage.Define(
                    LogLevel.Error,
                    EventIds.DiscoveryLoopFailed,
                    "Swallowing unhandled exception from Kubernetes loop...");

            public static void ErrorSignalingChange(ILogger logger, Exception exception)
            {
                _errorSignalingChange(logger, exception);
            }

            public static void StartingKubernetesDiscoveryLoop(ILogger<KubernetesConfigProvider> logger)
            {
                _startingKubernetesDiscoveryLoop(logger, null);
            }

            public static void KubernetesDiscoveryLoopEndedGracefully(ILogger<KubernetesConfigProvider> logger)
            {
                _kubernetesDiscoveryLoopEndedGracefully(logger, null);
            }

            public static void KubernetesDiscoveryLoopFailed(ILogger<KubernetesConfigProvider> logger, Exception exception)
            {
                _kubernetesDiscoveryLoopFailed(logger, exception);
            }
        }
    }
}
