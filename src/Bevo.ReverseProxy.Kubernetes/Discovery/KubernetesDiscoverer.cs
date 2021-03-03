// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.ReverseProxy.Abstractions;
using Microsoft.ReverseProxy.Service;
using Newtonsoft.Json;

namespace Bevo.ReverseProxy.Kube
{
    public class KubernetesDiscoverer : IKubernetesDiscoverer, IDisposable
    {
        private const string MatchingIngressClass = "dotnet";

        private readonly ILogger<KubernetesDiscoverer> _logger;

        private readonly Kubernetes _client;

        private readonly IConfigValidator _configValidator;

        private string _podName;

        private string _podNamespace;

        private string _publishService;

        public KubernetesDiscoverer(IConfigValidator configValidator, ILogger<KubernetesDiscoverer> logger)
        {
            _logger = logger;
            _configValidator = configValidator;

            var config = this.LocateKubeConfig();
            _client = new Kubernetes(config);
        }

        public async Task<DiscoveredItems> DiscoverAsync(CancellationToken cancellation)
        {
            var discoveredClusters = new Dictionary<string, Cluster>(StringComparer.Ordinal);
            var discoveredRoutes = new List<ProxyRoute>();
            IEnumerable<IngressModel> ingresses;
            IEnumerable<ServicePortModel> servicePorts;

            try
            {
                ingresses = await this.FindMatchingIngressesAsync(cancellation);
                servicePorts = await this.FindServicesAndPortsAsync(ingresses, cancellation);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.GettingApplicationFailed(_logger, ex);
                ingresses = Enumerable.Empty<IngressModel>();
                servicePorts = Enumerable.Empty<ServicePortModel>();
            }

            foreach (var sp in servicePorts)
            {
                try
                {
                    var endpoints = await _client.ListNamespacedEndpointsAsync(
                        namespaceParameter: sp.Namespace,
                        fieldSelector: $"metadata.name={sp.ServiceName}",
                        cancellationToken: cancellation);

                    var matchedEndpoint = endpoints.Items.FirstOrDefault();
                    if (matchedEndpoint == null)
                    {
                        _logger.LogWarning($"No endpoint found for service {sp.ServiceName} in namespace {sp.Namespace}");
                        continue;
                    }

                    // Do we have a matching ingress for this port?
                    var matchingIngresses = FindIngressesForService(sp, ingresses);

                    if (matchingIngresses.Any())
                    {
                        _logger.LogDebug($"Found matching ingresses for service {sp.ServiceName} in namespace {sp.Namespace}");

                        // Use the endpoint port number as this is guaranteed to be an int (unlike the service).
                        var clusterId = $"{sp.ServiceName}.{sp.Namespace}:{sp.Port}";

                        // Cluster
                        var cluster = BuildCluster(clusterId, sp, matchedEndpoint);
                        var clusterValidationErrors = await _configValidator.ValidateClusterAsync(cluster);
                        if (clusterValidationErrors.Count > 0)
                        {
                            throw new ConfigException($"Skipping cluster id '{cluster.Id} due to validation errors.", new AggregateException(clusterValidationErrors));
                        }

                        if (!discoveredClusters.TryAdd(cluster.Id, cluster))
                        {
                            throw new ConfigException($"Duplicated cluster id '{cluster.Id}'. Skipping repeated definition, service '{sp.ServiceName}' in namespace '{sp.Namespace}'");
                        }

                        // Routes
                        var routes = BuildRoutes(clusterId, matchingIngresses);
                        var routeValidationErrors = new List<Exception>();
                        foreach (var route in routes)
                        {
                            routeValidationErrors.AddRange(await _configValidator.ValidateRouteAsync(route));
                        }

                        if (routeValidationErrors.Count > 0)
                        {
                            // Don't add ANY routes if even a single one is bad. Trying to add partial routes
                            // could lead to unexpected results (e.g. a typo in the configuration of higher-priority route
                            // could lead to a lower-priority route being selected for requests it should not be handling).
                            throw new ConfigException($"Skipping ALL routes for cluster id '{cluster.Id} due to validation errors.", new AggregateException(routeValidationErrors));
                        }

                        discoveredRoutes.AddRange(routes);
                    }
                }
                catch (Exception ex)
                {
                    // Not user's problem
                    Log.ErrorLoadingEndpoints(_logger, sp.Namespace, sp.ServiceName, ex);
                }
            }

            await ReportStatus(ingresses, cancellation);

            Log.ServiceDiscovered(_logger, discoveredClusters.Count, discoveredRoutes.Count);
            return new DiscoveredItems(discoveredRoutes, discoveredClusters.Values.ToList());
        }

        public void Dispose()
        {
            _client?.Dispose();
        }

        private async Task ReportStatus(IEnumerable<IngressModel> ingresses, CancellationToken cancellation)
        {
            var matchedServiceInfo = await _client.ListNamespacedServiceAsync(_podNamespace, fieldSelector: $"metadata.name={_publishService}", cancellationToken: cancellation);
            var myServiceInfo = matchedServiceInfo?.Items?.FirstOrDefault();
            if (myServiceInfo == null)
            {
                _logger.LogError("Failed to locate my service {service}/{namespace}", _publishService, _podNamespace);
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

                    await _client.ReplaceNamespacedIngressStatusAsync(ingress.Original, name: ingress.Name, namespaceParameter: ingress.Namespace, cancellationToken: cancellation);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "failed to patch ingress status for {ingress}/{namespace}", ingress.Name, ingress.Namespace);
                }
            }
        }

        private Cluster BuildCluster(string clusterId, ServicePortModel sp, V1Endpoints endpoints) // V1EndpointPort port, V1EndpointSubset subset)
        {
            var destinations = new Dictionary<string, Destination>();

            var destinationIndex = 0;

            foreach (var subset in endpoints.Subsets)
            {
                foreach (var port in subset.Ports)
                {
                    if (string.IsNullOrWhiteSpace(sp.PortName))
                    {
                        // Compare based on port number
                        if (sp.Port == port.Port)
                        {
                            // TODO - Check the scheme! Currently assuming http. Also figure out what to do with unavailable addresses
                            var addresses = subset.Addresses.Select(a => $"http://{a.Ip}:{port.Port}").ToArray();
                            for (var i = 0; i < addresses.Length; i++)
                            {
                                var dest = new Destination()
                                {
                                    Address = addresses[i],
                                };

                                destinations.Add($"{clusterId}/{destinationIndex}", dest);
                                destinationIndex++;
                            }
                        }
                    }
                    else
                    {
                        if (sp.PortName == port.Name)
                        {
                            // TODO - Check the scheme! Currently assuming http. Also figure out what to do with unavailable addresses
                            var addresses = subset.Addresses.Select(a => $"http://{a.Ip}:{port.Port}").ToArray();
                            for (var i = 0; i < addresses.Length; i++)
                            {
                                var dest = new Destination()
                                {
                                    Address = addresses[i],
                                };

                                destinations.Add($"{clusterId}/{destinationIndex}", dest);
                                destinationIndex++;
                            }
                        }
                    }
                }
            }

            return new Cluster
            {
                Id = clusterId,
                Destinations = destinations,
            };
        }

        private IEnumerable<ProxyRoute> BuildRoutes(string clusterId, IEnumerable<IngressModel> matchingIngresses)
        {
            var routes = new List<ProxyRoute>();

            var routeIndex = 0;

            foreach (var ingress in matchingIngresses)
            {
                foreach (var rule in ingress.Rules)
                {
                    foreach (var path in rule.Paths)
                    {
                        ProxyMatch match = null;

                        switch (path.PathType)
                        {
                            case IngressPath.IngressPathType.Prefix:
                                match = new ProxyMatch()
                                {
                                    Hosts = new[] { rule.Host },
                                    Path = path.Path + "{**catch-all}",
                                };
                                break;

                            case IngressPath.IngressPathType.Exact:
                                match = new ProxyMatch()
                                {
                                    Hosts = new[] { rule.Host },

                                    // TODO Is this case sensitive? K8S specification says "Exact" should be
                                    Path = path.Path,
                                };
                                break;

                            default:
                                _logger.LogWarning($"Unexpected Ingress `PathType` value for {ingress.Namespace}/{ingress.Name}");
                                break;
                        }

                        if (match != null)
                        {
                            var route = new ProxyRoute()
                            {
                                ClusterId = clusterId,
                                //AuthorizationPolicy = "default",
                                RouteId = $"{clusterId}/{routeIndex}",
                                Match = match,
                            };

                            routes.Add(route);
                            routeIndex++;
                        }
                    }
                }
            }

            return routes;
        }

        private IEnumerable<IngressModel> FindIngressesForService(ServicePortModel sp, IEnumerable<IngressModel> ingresses)
        {
            var matchingIngresses = new List<IngressModel>();

            // Match ingresses in the right namespace
            foreach (var ingress in ingresses.Where(i => i.Namespace == sp.Namespace))
            {
                foreach (var rule in ingress.Rules)
                {
                    // Match paths with the right service name
                    foreach (var path in rule.Paths.Where(p => p.BackendServiceName == sp.ServiceName && p.PathType != IngressPath.IngressPathType.ImplementationSpecific))
                    {
                        if (int.TryParse(path.BackendServicePort, out var portValue) && portValue == sp.Port)
                        {
                            // Port number match
                            matchingIngresses.Add(ingress);
                        }
                        else if (string.Equals(path.BackendServicePort, sp.PortName, StringComparison.OrdinalIgnoreCase))
                        {
                            // Matched based on the port name in the service
                            matchingIngresses.Add(ingress);
                        }
                    }
                }
            }

            return matchingIngresses;
        }

        private KubernetesClientConfiguration LocateKubeConfig()
        {
            // Attempt to dynamically determine between in-cluster and host (debug) development...
            var serviceHost = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
            var servicePort = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_PORT");

            // Locate from environment variables directly. Unlike IConfiguration, which could be overridden.
            _podName = Environment.GetEnvironmentVariable("POD_NAME");
            _podNamespace = Environment.GetEnvironmentVariable("POD_NAMESPACE");
            _publishService = Environment.GetEnvironmentVariable("PUBLISH_SERVICE");

            if (string.IsNullOrWhiteSpace(_podName) || string.IsNullOrWhiteSpace(_podNamespace))
            {
                throw new InvalidOperationException("Failed to detect current pod information. Check POD_NAME and POD_NAMESPACE environment variables.");
            }

            if (!string.IsNullOrWhiteSpace(serviceHost) && !string.IsNullOrWhiteSpace(servicePort))
            {
                return KubernetesClientConfiguration.InClusterConfig();
            }

            return KubernetesClientConfiguration.BuildConfigFromConfigFile();
        }

        private async Task<IEnumerable<IngressModel>> FindMatchingIngressesAsync(CancellationToken cancellation)
        {
            var ingresses = await _client.ListIngressForAllNamespacesAsync(cancellationToken: cancellation);

            // Looking for both v1.18 and deprecated mechanisms of identifying ingress class. See https://kubernetes.io/blog/2020/04/02/improvements-to-the-ingress-api-in-kubernetes-1.18/
            var matchedIngresses = ingresses.Items
                .Where(i => IngressMatch(i))
                .OrderBy(i => i.Metadata.CreationTimestamp)
                .Select(i => i.ToModel());

            var ingressDump = new StringBuilder();
            ingressDump.AppendLine($"Located {matchedIngresses.Count()} ingresses:");

            foreach (var ingress in matchedIngresses)
            {
                foreach (var rule in ingress.Rules)
                {
                    ingressDump.AppendLine($"\tRule {rule.Host}");
                    foreach (var path in rule.Paths)
                    {
                        ingressDump.AppendLine($"\t\tPath ({path.PathType}) '{path.Path}' => {path.BackendServiceName}.{ingress.Namespace}:{path.BackendServicePort}");
                    }
                }
            }

            _logger.LogInformation(ingressDump.ToString());

            return matchedIngresses;
        }

        private async Task<IEnumerable<ServicePortModel>> FindServicesAndPortsAsync(IEnumerable<IngressModel> ingresses, CancellationToken cancellation)
        {
            var uniqueServices = new Dictionary<string, V1Service>(StringComparer.InvariantCultureIgnoreCase);
            var servicePortModels = new Dictionary<string, ServicePortModel>();

            foreach (var ingress in ingresses)
            {
                foreach (var rule in ingress.Rules)
                {
                    // Only support TCP ports so far.
                    foreach (var path in rule.Paths)
                    {
                        var serviceName = $"{ingress.Namespace}.{path.BackendServiceName}";

                        if (!uniqueServices.TryGetValue(serviceName, out V1Service locatedService))
                        {
                            var k8sService = await _client.ListNamespacedServiceAsync(
                                namespaceParameter: ingress.Namespace,
                                fieldSelector: $"metadata.name={path.BackendServiceName}",
                                cancellationToken: cancellation);

                            // We should only get one service given we have a tight fieldSelector
                            locatedService = k8sService.Items.FirstOrDefault();
                            if (locatedService != null)
                            {
                                uniqueServices.Add(serviceName, locatedService);
                            }
                        }

                        if (locatedService != null)
                        {
                            // Only add TCP ports
                            foreach (var port in locatedService.Spec.Ports.Where(p => p.Protocol.ToUpperInvariant() == "TCP"))
                            {
                                // Use port.Port as the port.Name can be null
                                var portKey = $"{serviceName}-{port.Port}";
                                if (!servicePortModels.ContainsKey(portKey))
                                {
                                    var spm = port.ToModel(locatedService.Metadata.Name, locatedService.Metadata.NamespaceProperty);
                                    servicePortModels.Add(portKey, spm);
                                }
                            }
                        }
                    }
                }
            }

            return servicePortModels.Values;
        }

        private bool IngressMatch(Extensionsv1beta1Ingress ingress)
        {
            return string.Equals(ingress.Spec.IngressClassName, MatchingIngressClass, StringComparison.OrdinalIgnoreCase) ||
                ingress.Metadata.Annotations.TryGetValue("kubernetes.io/ingress.class", out var ingressClass) && string.Equals(ingressClass, MatchingIngressClass, StringComparison.OrdinalIgnoreCase);
        }

        private static class Log
        {
            private static readonly Action<ILogger, Exception> _gettingKubernetesApplicationFailed =
                LoggerMessage.Define(
                    LogLevel.Error,
                    EventIds.GettingApplicationFailed,
                    "Could not get applications list from Kubernetes, continuing with zero applications.");

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

            private static readonly Action<ILogger, string, string, Exception> _errorLoadingEndpoints =
                LoggerMessage.Define<string, string>(
                    LogLevel.Error,
                    EventIds.ErrorLoadingServiceConfig,
                    "Unexpected error when trying to load endpoints for service '{serviceName}' in namespace '{namespaceName}', skipping.");

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
                _gettingKubernetesApplicationFailed(logger, exception);
            }

            public static void GettingServiceFailed(ILogger logger, Uri application, Exception exception)
            {
                _gettingServiceFailed(logger, application, exception);
            }

            public static void InvalidServiceConfig(ILogger logger, Uri service, Exception exception)
            {
                _invalidServiceConfig(logger, service, exception);
            }

            public static void ErrorLoadingEndpoints(ILogger logger, string namespaceName, string serviceName, Exception exception)
            {
                _errorLoadingEndpoints(logger, namespaceName, serviceName, exception);
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
    }
}
