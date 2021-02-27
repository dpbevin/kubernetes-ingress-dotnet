using k8s;
using Microsoft.Extensions.Logging;
using Microsoft.ReverseProxy.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bevo.ReverseProxy.Kube.Discovery
{
    public class KubernetesDiscoverer : IKubernetesDiscoverer
    {
        private const string MatchingIngressClass = "nginx";

        private readonly ILogger<KubernetesDiscoverer> _logger;

        private readonly Kubernetes _client;

        public KubernetesDiscoverer(ILogger<KubernetesDiscoverer> logger)
        {
            _logger = logger;

            var config = this.LocateKubeConfig();
            _client = new Kubernetes(config);
        }

        public async Task<(IReadOnlyList<ProxyRoute> Routes, IReadOnlyList<Cluster> Clusters)> DiscoverAsync(CancellationToken cancellation)
        {
            var discoveredBackends = new Dictionary<string, Cluster>(StringComparer.Ordinal);
            var discoveredRoutes = new List<ProxyRoute>();
            var matchedServices = new List<MatchedService>();

            try
            {
                var matchingIngresses = await this.FindMatchingIngressesAsync(cancellation);

                await this.Discover(matchingIngresses, matchedServices, discoveredBackends, discoveredRoutes);

                var matchingNamespaces = matchedServices.Select(i => i.Namespace).Distinct();

                foreach (var ns in matchingNamespaces)
                {
                    var endponts = await _client.ListNamespacedEndpointsAsync(ns);

                    var pods = await _client.ListNamespacedPodAsync(ns);
                    var runningPods = pods.Items.Where(p => p.Status.Phase.Equals("Running", StringComparison.OrdinalIgnoreCase)).ToArray();

                    var sb = new StringBuilder();
                    sb.AppendLine($"Found {endponts.Items.Count} endponts in namespace {ns}");
                    foreach (var e in endponts.Items)
                    {
                        var firstSubset = e.Subsets.First();
                        string addresses = string.Empty;

                        if (firstSubset.Addresses != null)
                        {
                            addresses = string.Join(",", firstSubset.Addresses.Select(a => $"{a.Ip}:{firstSubset.Ports[0].Port}"));
                        }

                        var msg = firstSubset.NotReadyAddresses != null ? "NOT READY: " + string.Join(",", firstSubset.NotReadyAddresses.Select(a => a.Ip)) : string.Empty;
                        sb.AppendLine($"\t{e.Metadata.Name} has addresses {addresses} {msg}");
                    }

                    _logger.LogInformation(sb.ToString());
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.GettingApplicationFailed(_logger, ex);
            }

            Log.ServiceDiscovered(_logger, discoveredBackends.Count, discoveredRoutes.Count);
            return (discoveredRoutes, discoveredBackends.Values.ToList());
        }

        private KubernetesClientConfiguration LocateKubeConfig()
        {
            // Attempt to dynamically determine between in-cluster and host (debug) development...
            var serviceHost = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
            var servicePort = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");

            if (!string.IsNullOrWhiteSpace(serviceHost) && !string.IsNullOrWhiteSpace(servicePort))
            {
                return KubernetesClientConfiguration.InClusterConfig();
            }

            return KubernetesClientConfiguration.BuildConfigFromConfigFile();
        }

        private async Task<IEnumerable<k8s.Models.Extensionsv1beta1Ingress>> FindMatchingIngressesAsync(CancellationToken cancellation)
        {
            var ingresses = await _client.ListIngressForAllNamespacesAsync(cancellationToken: cancellation);

            var matchedIngresses = ingresses.Items
                .Where(i => i.Metadata.Annotations.TryGetValue("kubernetes.io/ingress.class", out var ingressClass) && string.Equals(ingressClass, MatchingIngressClass, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.Metadata.CreationTimestamp)
                .ToArray();

            var ingressDump = new StringBuilder();
            ingressDump.AppendLine($"Located {matchedIngresses.Length} ingresses:");

            foreach (var ingress in matchedIngresses)
            {
                foreach (var rule in ingress.Spec.Rules)
                {
                    ingressDump.AppendLine($"\tRule {rule.Host}");
                    foreach (var path in rule.Http.Paths)
                    {
                        var supported = path.Path.Equals("/") ? string.Empty : "(NOT SUPPORTED) ";

                        ingressDump.AppendLine($"\t\t{supported}Path '{path.Path}' => {path.Backend.ServiceName}.{ingress.Metadata.NamespaceProperty}:{path.Backend.ServicePort.Value}");
                    }
                }
            }

            _logger.LogDebug(ingressDump.ToString());

            return matchedIngresses;
        }

        private async Task Discover(
            IEnumerable<k8s.Models.Extensionsv1beta1Ingress> matchingIngresses,
            List<MatchedService> matchedServices,
            IDictionary<string, Cluster> discoveredBackends,
            IList<ProxyRoute> discoveredRoutes)
        {
            foreach (var ingress in matchingIngresses)
            {
                foreach (var rule in ingress.Spec.Rules)
                {
                    foreach (var path in rule.Http.Paths)
                    {
                        // Only supporting the easy stuff now
                        if (path.Path.Equals("/"))
                        {
                            var serviceName = $"{path.Backend.ServiceName}.{ingress.Metadata.NamespaceProperty}/{path.Backend.ServicePort.Value}";
                            var backendCreated = true;

                            if (!discoveredBackends.TryGetValue(serviceName, out var backend))
                            {
                                var destinations = new Dictionary<string, Destination>();

                                string address = null;

                                if (int.TryParse(path.Backend.ServicePort.Value, out var portNumber))
                                {
                                    // TODO - Check the scheme! Currently assuming http
                                    address = $"http://{path.Backend.ServiceName}.{ingress.Metadata.NamespaceProperty}.svc.cluster.local:{portNumber}";
                                }
                                else
                                {
                                    var k8sService = await _client.ListNamespacedServiceAsync(namespaceParameter: ingress.Metadata.NamespaceProperty, fieldSelector: $"metadata.name={path.Backend.ServiceName}");
                                    var firstService = k8sService.Items.FirstOrDefault();
                                    if (firstService != null)
                                    {
                                        var matchedPort = firstService.Spec.Ports.FirstOrDefault(p => p.Name.Equals(path.Backend.ServicePort.Value, StringComparison.OrdinalIgnoreCase));

                                        // TODO - Check the scheme!
                                        address = $"http://{path.Backend.ServiceName}.{ingress.Metadata.NamespaceProperty}.svc.cluster.local:{matchedPort.Port}";
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(address))
                                {
                                    var dest = new Destination()
                                    {
                                        Address = address,
                                    };

                                    destinations.Add($"{serviceName}/destination1", dest);

                                    backend = new Cluster
                                    {
                                        Id = serviceName,
                                        Destinations = destinations,
                                    };

                                    discoveredBackends.Add(serviceName, backend);
                                    matchedServices.Add(new MatchedService { Namespace = ingress.Metadata.NamespaceProperty, ServiceName = path.Backend.ServiceName });
                                }
                                else
                                {
                                    backendCreated = false;
                                }
                            }

                            if (backendCreated)
                            {
                                // Add the route
                                var route = new ProxyRoute()
                                {
                                    ClusterId = serviceName,
                                    //AuthorizationPolicy = "default",
                                    RouteId = $"{serviceName}-allroutes",
                                    Match = new ProxyMatch()
                                    {
                                        Hosts = new[] { rule.Host },
                                        Path = "{**catch-all}",
                                    }
                                };

                                discoveredRoutes.Add(route);
                            }
                        }
                    }
                }
            }
        }

        private static class Log
        {
            private static readonly Action<ILogger, Exception> _gettingServiceFabricApplicationFailed =
                LoggerMessage.Define(
                    LogLevel.Error,
                    EventIds.GettingApplicationFailed,
                    "Could not get applications list from Service Fabric, continuing with zero applications.");

            private static readonly Action<ILogger, Uri, Exception> _gettingServiceFailed =
                LoggerMessage.Define<Uri>(
                    LogLevel.Error,
                    EventIds.GettingServiceFailed,
                    "Could not get service list for application '{applicationName}', skipping application.");

            private static readonly Action<ILogger, Uri, Exception> _invalidServiceConfig =
                LoggerMessage.Define<Uri>(
                    LogLevel.Information,
                    EventIds.InvalidServiceConfig,
                    "Config error found when trying to load service '{serviceName}', skipping.");

            private static readonly Action<ILogger, Uri, Exception> _errorLoadingServiceConfig =
                LoggerMessage.Define<Uri>(
                    LogLevel.Error,
                    EventIds.ErrorLoadingServiceConfig,
                    "Unexpected error when trying to load service '{serviceName}', skipping.");

            private static readonly Action<ILogger, int, int, Exception> _serviceDiscovered =
                LoggerMessage.Define<int, int>(
                    LogLevel.Information,
                    EventIds.ServiceDiscovered,
                    "Discovered '{discoveredBackendsCount}' backends, '{discoveredRoutesCount}' routes.");

            private static readonly Action<ILogger, Uri, Exception> _gettingPartitionFailed =
                LoggerMessage.Define<Uri>(
                    LogLevel.Error,
                    EventIds.GettingPartitionFailed,
                    "Could not get partition list for service '{serviceName}', skipping endpoints.");

            private static readonly Action<ILogger, Guid, Uri, Exception> _gettingReplicaFailed =
                LoggerMessage.Define<Guid, Uri>(
                    LogLevel.Error,
                    EventIds.GettingReplicaFailed,
                    "Could not get replica list for partition '{partition}' of service '{serviceName}', skipping partition.");

            private static readonly Action<ILogger, long, Uri, Exception> _invalidReplicaConfig =
                LoggerMessage.Define<long, Uri>(
                    LogLevel.Information,
                    EventIds.InvalidReplicaConfig,
                    "Config error found when trying to build endpoint for replica '{replicaId}' of service '{serviceName}', skipping.");

            private static readonly Action<ILogger, long, Uri, Exception> _errorLoadingReplicaConfig =
                LoggerMessage.Define<long, Uri>(
                    LogLevel.Error,
                    EventIds.ErrorLoadingReplicaConfig,
                    "Could not build endpoint for replica '{replicaId}' of service '{serviceName}'.");

            private static readonly Action<ILogger, string, Uri, Exception> _invalidReplicaSelectionMode =
                LoggerMessage.Define<string, Uri>(
                    LogLevel.Warning,
                    EventIds.InvalidReplicaSelectionMode,
                    "Invalid replica selection mode: '{statefulReplicaSelectionMode}' for service '{serviceName}', fallback to selection mode: All.");

            public static void GettingApplicationFailed(ILogger logger, Exception exception)
            {
                _gettingServiceFabricApplicationFailed(logger, exception);
            }

            public static void GettingServiceFailed(ILogger logger, Uri application, Exception exception)
            {
                _gettingServiceFailed(logger, application, exception);
            }

            public static void InvalidServiceConfig(ILogger logger, Uri service, Exception exception)
            {
                _invalidServiceConfig(logger, service, exception);
            }

            public static void ErrorLoadingServiceConfig(ILogger logger, Uri service, Exception exception)
            {
                _errorLoadingServiceConfig(logger, service, exception);
            }

            public static void ServiceDiscovered(ILogger logger, int discoveredBackendsCount, int discoveredRoutesCount)
            {
                _serviceDiscovered(logger, discoveredBackendsCount, discoveredRoutesCount, null);
            }

            public static void GettingPartitionFailed(ILogger logger, Uri service, Exception exception)
            {
                _gettingPartitionFailed(logger, service, exception);
            }

            public static void GettingReplicaFailed(ILogger logger, Guid partition, Uri service, Exception exception)
            {
                _gettingReplicaFailed(logger, partition, service, exception);
            }

            public static void InvalidReplicaConfig(ILogger<KubernetesDiscoverer> logger, long replicaId, Uri serviceName, Exception exception)
            {
                _invalidReplicaConfig(logger, replicaId, serviceName, exception);
            }

            public static void ErrorLoadingReplicaConfig(ILogger<KubernetesDiscoverer> logger, long replicaId, Uri serviceName, Exception exception)
            {
                _errorLoadingReplicaConfig(logger, replicaId, serviceName, exception);
            }

            public static void InvalidReplicaSelectionMode(ILogger<KubernetesDiscoverer> logger, string statefulReplicaSelectionMode, Uri serviceName)
            {
                _invalidReplicaSelectionMode(logger, statefulReplicaSelectionMode, serviceName, null);
            }
        }

        private class MatchedService
        {
            public string Namespace { get; set; }

            public string ServiceName { get; set; }
        }
    }
}
