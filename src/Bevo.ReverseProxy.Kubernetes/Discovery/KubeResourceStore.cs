// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace Bevo.ReverseProxy.Kube
{
    public class KubeResourceStore : IKubeResourceStore, IDisposable
    {
        private const string MatchingIngressClass = "dotnet";

        private readonly IKubernetes _client;

        private readonly ILogger _logger;

        private readonly IEventRecorder _eventRecorder;

        private readonly CancellationTokenSource _backgroundCts;

        private readonly Task _backgroundTask;

        private readonly ConcurrentDictionary<string, IngressModel> _ingresses = new ConcurrentDictionary<string, IngressModel>();

        public KubeResourceStore(IKubernetes client, IEventRecorder eventRecorder, ILogger<KubeResourceStore> logger)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _eventRecorder = eventRecorder ?? throw new ArgumentNullException(nameof(eventRecorder));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _backgroundCts = new CancellationTokenSource();
            _backgroundTask = Run();
        }

        public IEnumerable<IngressModel> Ingresses => _ingresses.Values;

        public string GetBackendConfiguration()
        {
            return "hello";
        }

        public void Dispose()
        {
            _backgroundTask.Dispose();
            _backgroundCts?.Dispose();
        }

        private async Task Run()
        {
            var cancellation = _backgroundCts.Token;

            while (true)
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();

                    var ingressesResponse = _client.ListIngressForAllNamespaces1WithHttpMessagesAsync(watch: true, cancellationToken: cancellation);
                    using (ingressesResponse.Watch<V1Ingress, V1IngressList>((type, item) =>
                    {
                        switch (type)
                        {
                            case WatchEventType.Added:
                                AddIngress(item);
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
                        _logger.LogTrace("Store waiting for 5 minutes");
                        await Task.Delay(TimeSpan.FromMinutes(5), cancellation);
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // Graceful shutdown
                    _logger.LogInformation("Shutting down");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Ingress watch failed");
                }
            }
        }

        private void AddIngress(V1Ingress ingress)
        {
            if (!IngressMatch(ingress))
            {
                _logger.LogInformation($"Ignoring ingress ingress={ingress.Metadata.NamespaceProperty}/{ingress.Metadata.Name} kubernetes.io/ingress.class=TODO ingressClassName={ingress.Spec.IngressClassName}");
                return;
            }

            //_eventRecorder.CreateEvent();

            var model = ingress.ToModel();
            var key = ingress.Metadata.Uid;
            _ingresses.AddOrUpdate(key, a => model, (s, u) => model);
        }

        private void DeleteIngress(V1Ingress ingress)
        {
            var key = ingress.Metadata.Uid;
            _ingresses.TryRemove(key, out var dummy);
        }

        private void UpdateIngress(V1Ingress ingress)
        {
            var model = ingress.ToModel();
            var key = ingress.Metadata.Uid;
            _ingresses.AddOrUpdate(key, a => model, (s, u) => model);
        }

        private bool IngressMatch(V1Ingress ingress)
        {
            return string.Equals(ingress.Spec.IngressClassName, MatchingIngressClass, StringComparison.OrdinalIgnoreCase) ||
                ingress.Metadata.Annotations.TryGetValue("kubernetes.io/ingress.class", out var ingressClass) && string.Equals(ingressClass, MatchingIngressClass, StringComparison.OrdinalIgnoreCase);
        }
    }
}
