// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using k8s;
using k8s.LeaderElection;
using k8s.LeaderElection.ResourceLock;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bevo.ReverseProxy.Kube
{
    public class StatusReporterService : BackgroundService
    {
        private readonly IKubeResourceStore _store;

        private readonly IKubernetes _client;

        private readonly IControllerConfiguration _configuration;

        private readonly ILogger _logger;

        private Task _electorTask;

        private ManualResetEventSlim _leaderEvent = new ManualResetEventSlim();

        public StatusReporterService(IKubeResourceStore store, IKubernetes client, IControllerConfiguration configuration, ILogger<StatusReporterService> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _electorTask = StartLeaderElector(stoppingToken);

            while (true)
            {
                try
                {
                    _leaderEvent.Wait(stoppingToken);

                    await ReportStatus(stoppingToken);

                    await Task.Delay(_configuration.StatusReportInterval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Graceful shutdown
                    _logger.LogInformation("Shutting down");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected exception");
                }
            }
        }

        public override void Dispose()
        {
            _leaderEvent?.Dispose();
            _leaderEvent = null;

            base.Dispose();
        }

        private Task StartLeaderElector(CancellationToken stoppingToken)
        {
            var configMapLock = new ConfigMapLock(_client, _configuration.PodNamespace, _configuration.ElectionConfigMapName, _configuration.PodName);

            var leaderElectionConfig = new LeaderElectionConfig(configMapLock)
            {
                LeaseDuration = TimeSpan.FromMilliseconds(1000),
                RetryPeriod = TimeSpan.FromMilliseconds(500),
                RenewDeadline = TimeSpan.FromMilliseconds(600),
            };

            return Task.Run(() =>
            {
                var leaderElector = new LeaderElector(leaderElectionConfig);

                leaderElector.OnStartedLeading += () =>
                {
                    _logger.LogTrace("I am the leader");
                    _leaderEvent.Set();
                };

                leaderElector.OnStoppedLeading += () =>
                {
                    _logger.LogTrace("I am NOT the leader");
                    _leaderEvent.Reset();
                };

                while (!stoppingToken.IsCancellationRequested)
                {
                    leaderElector.RunAsync().Wait(stoppingToken);
                }

                _logger.LogTrace("Election finished");
            },
            stoppingToken);
        }

        private async Task ReportStatus(CancellationToken cancellation)
        {
            var ingresses = _store.Ingresses;

            var matchedServiceInfo = await _client.ListNamespacedServiceAsync(_configuration.PodNamespace, fieldSelector: $"metadata.name={_configuration.PublishService}", cancellationToken: cancellation);
            var myServiceInfo = matchedServiceInfo?.Items?.FirstOrDefault();
            if (myServiceInfo == null)
            {
                _logger.LogError("Failed to locate my service {service}/{namespace}", _configuration.PublishService, _configuration.PodNamespace);
                return;
            }

            foreach (var ingress in ingresses)
            {
                _logger.LogInformation(
                    "Updating Ingress status: namespace=\"{namespace}\" ingress=\"{ingress}, currentValue={currentValue}, newValue={newValue}",
                    ingress.Namespace,
                    ingress.Name,
                    JsonConvert.SerializeObject(ingress.Original.Status.LoadBalancer.Ingress),
                    JsonConvert.SerializeObject(myServiceInfo.Status.LoadBalancer.Ingress));

                try
                {
                    ingress.Original.Status.LoadBalancer.Ingress = myServiceInfo.Status.LoadBalancer.Ingress;

                    await _client.ReplaceNamespacedIngressStatus1Async(ingress.Original, name: ingress.Name, namespaceParameter: ingress.Namespace, cancellationToken: cancellation);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "failed to patch ingress status for {ingress}/{namespace}", ingress.Name, ingress.Namespace);
                }
            }
        }
    }
}
