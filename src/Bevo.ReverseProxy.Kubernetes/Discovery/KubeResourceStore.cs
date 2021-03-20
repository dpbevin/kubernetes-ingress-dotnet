// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.ReverseProxy.Service;

namespace Bevo.ReverseProxy.Kube
{
    public class KubeResourceStore : IKubeResourceStore, IDisposable
    {
        private const string MatchingIngressClass = "dotnet";

        private readonly IKubernetes _client;

        private readonly ILogger _logger;

        private readonly IEventRecorder _eventRecorder;

        private readonly CancellationTokenSource _backgroundCts;

        private readonly Task _watchIngressesTask;

        private readonly Task _watchServicesTask;

        private readonly Task _watchEndpointsTask;

        private readonly ConcurrentDictionary<string, IngressModel> _ingresses = new ConcurrentDictionary<string, IngressModel>();

        private readonly ConcurrentDictionary<string, V1Service> _services = new ConcurrentDictionary<string, V1Service>();

        private readonly ConcurrentDictionary<string, V1Endpoints> _endpoints = new ConcurrentDictionary<string, V1Endpoints>();

        private ConfigurationReloadToken _changeToken = new ConfigurationReloadToken();

        public KubeResourceStore(IKubernetes client, IEventRecorder eventRecorder, ILogger<KubeResourceStore> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _eventRecorder = eventRecorder ?? throw new ArgumentNullException(nameof(eventRecorder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _backgroundCts = new CancellationTokenSource();
            _watchIngressesTask = WatchIngresses();
            _watchServicesTask = WatchServices();
            _watchEndpointsTask = WatchServiceEndpoints();
        }

        public IEnumerable<IngressModel> Ingresses => _ingresses.Values;

        public IChangeToken ChangeToken => _changeToken;

        public async Task<V1Endpoints> GetEndpoint(string namespaceName, string serviceName, CancellationToken cancellation)
        {
            var key = $"{namespaceName}/{serviceName}";

            if (_endpoints.TryGetValue(key, out var matchedEndpoint))
            {
                return matchedEndpoint;
            }

            _logger.LogTrace($"Endpoint cache miss for key {key}");

            var endpoints = await _client.ListNamespacedEndpointsAsync(
                namespaceParameter: namespaceName,
                fieldSelector: $"metadata.name={serviceName}",
                cancellationToken: cancellation);

            return endpoints.Items.FirstOrDefault();
        }

        public async Task<V1Service> GetService(string namespaceName, string serviceName, CancellationToken cancellation)
        {
            var key = $"{namespaceName}/{serviceName}";

            if (_services.TryGetValue(key, out var matchedService))
            {
                return matchedService;
            }

            _logger.LogTrace($"Service cache miss for key {key}");

            var k8sService = await _client.ListNamespacedServiceAsync(
                namespaceParameter: namespaceName,
                fieldSelector: $"metadata.name={serviceName}",
                cancellationToken: cancellation);

            // We should only get one service given we have a tight fieldSelector
            return k8sService.Items.FirstOrDefault();
        }

        public void Dispose()
        {
            _watchIngressesTask?.Dispose();
            _watchEndpointsTask?.Dispose();
            _watchServicesTask?.Dispose();
            _backgroundCts?.Dispose();
        }

        private void RaiseChanged()
        {
            ConfigurationReloadToken previousToken = Interlocked.Exchange(ref _changeToken, new ConfigurationReloadToken());
            previousToken.OnReload();
        }

        private async Task WatchIngresses()
        {
            var cancellation = _backgroundCts.Token;

            while (true)
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();

                    var ingressesResponse = _client.ListIngressForAllNamespaces1WithHttpMessagesAsync(watch: true, cancellationToken: cancellation);
                    using (ingressesResponse.Watch<V1Ingress, V1IngressList>(async (type, item) =>
                    {
                        switch (type)
                        {
                            case WatchEventType.Added:
                                await AddIngress(item, cancellation);
                                break;

                            case WatchEventType.Deleted:
                                DeleteIngress(item);
                                break;

                            case WatchEventType.Modified:
                                UpdateIngress(item);
                                break;
                        }
                    }))
                    {
                        _logger.LogTrace("Ingress store waiting for 5 minutes");
                        await Task.Delay(TimeSpan.FromMinutes(5), cancellation);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // Graceful shutdown
                    _logger.LogInformation("Ingress store shutting down");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Ingress watch failed");
                }
            }
        }

        private async Task WatchServices()
        {
            var cancellation = _backgroundCts.Token;

            while (true)
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();

                    var servicesResponse = _client.ListServiceForAllNamespacesWithHttpMessagesAsync(watch: true, cancellationToken: cancellation);
                    using (servicesResponse.Watch<V1Service, V1ServiceList>((type, item) =>
                    {
                        var key = $"{item.Metadata.NamespaceProperty}/{item.Metadata.Name}";

                        switch (type)
                        {
                            case WatchEventType.Modified:
                            case WatchEventType.Added:
                                _services.AddOrUpdate(key, a => item, (s, u) => item);
                                break;

                            case WatchEventType.Deleted:
                                _services.TryRemove(key, out var dummy);
                                break;
                        }
                    }))
                    {
                        _logger.LogTrace("Service store waiting for 5 minutes");
                        await Task.Delay(TimeSpan.FromMinutes(5), cancellation);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // Graceful shutdown
                    _logger.LogInformation("Service store shutting down");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Service watch failed");
                }
            }
        }

        private async Task WatchServiceEndpoints()
        {
            var cancellation = _backgroundCts.Token;

            while (true)
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();

                    var serviceEndpointsResponse = _client.ListEndpointsForAllNamespacesWithHttpMessagesAsync(watch: true, cancellationToken: cancellation);
                    using (serviceEndpointsResponse.Watch<V1Endpoints, V1EndpointsList>((type, item) =>
                    {
                        var key = $"{item.Metadata.NamespaceProperty}/{item.Metadata.Name}";

                        switch (type)
                        {
                            case WatchEventType.Modified:
                            case WatchEventType.Added:
                                _endpoints.AddOrUpdate(key, a => item, (s, u) => item);
                                break;

                            case WatchEventType.Deleted:
                                _endpoints.TryRemove(key, out var dummy);
                                break;
                        }
                    }))
                    {
                        _logger.LogTrace("Endpoints store waiting for 5 minutes");
                        await Task.Delay(TimeSpan.FromMinutes(5), cancellation);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // Graceful shutdown
                    _logger.LogInformation("Endpoints store shutting down");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Endpoints watch failed");
                }
            }
        }

        private async Task AddIngress(V1Ingress ingress, CancellationToken cancellation)
        {
            var legacyIngressClass = ingress.LegacyIngressClass();
            if (!IngressMatch(ingress, legacyIngressClass))
            {
                _logger.LogInformation($"Ignoring ingress ingress={ingress.Metadata.NamespaceProperty}/{ingress.Metadata.Name} kubernetes.io/ingress.class={legacyIngressClass} ingressClassName={ingress.Spec.IngressClassName}");
                return;
            }

            var model = ingress.ToModel();
            var objRef = new V1ObjectReference
            {
                Kind = V1Ingress.KubeKind,
                ApiVersion = V1Ingress.KubeGroup + "/" + V1Ingress.KubeApiVersion,
                Name = model.Name,
                NamespaceProperty = model.Namespace,
                Uid = ingress.Metadata.Uid,
                ResourceVersion = ingress.Metadata.ResourceVersion,
            };

            await _eventRecorder.CreateEvent(objRef, KubeEvent.EventType.Normal, "Sync", "Scheduled for sync", cancellation);

            var key = ingress.Metadata.Uid;
            _ingresses.AddOrUpdate(key, a => model, (s, u) => model);

            RaiseChanged();
        }

        private void DeleteIngress(V1Ingress ingress)
        {
            var legacyIngressClass = ingress.LegacyIngressClass();
            if (!IngressMatch(ingress, legacyIngressClass))
            {
                return;
            }

            var key = ingress.Metadata.Uid;
            _ingresses.TryRemove(key, out var dummy);

            RaiseChanged();
        }

        private void UpdateIngress(V1Ingress ingress)
        {
            var legacyIngressClass = ingress.LegacyIngressClass();
            if (!IngressMatch(ingress, legacyIngressClass))
            {
                return;
            }

            var model = ingress.ToModel();
            var key = ingress.Metadata.Uid;
            _ingresses.AddOrUpdate(key, a => model, (s, u) => model);

            RaiseChanged();
        }

        private bool IngressMatch(V1Ingress ingress, string legacyIngressClass)
        {
            return string.Equals(ingress.Spec.IngressClassName, MatchingIngressClass, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(legacyIngressClass, MatchingIngressClass, StringComparison.OrdinalIgnoreCase);
        }
    }
}
