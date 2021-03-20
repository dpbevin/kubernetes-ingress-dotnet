// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.ReverseProxy.Abstractions;
using Microsoft.ReverseProxy.Service;

namespace Bevo.ReverseProxy.Kube
{
    public class IngressController : IIngressController
    {
        private readonly IKubeResourceStore _store;

        private readonly IConfigValidator _configValidator;

        private readonly ILogger _logger;

        public IngressController(IKubeResourceStore store, IConfigValidator configValidator, ILogger<IngressController> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _configValidator = configValidator ?? throw new ArgumentNullException(nameof(configValidator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IChangeToken ChangeToken => _store.ChangeToken;

        public async Task<BackendConfiguration> GetConfiguration(CancellationToken cancellation)
        {
            // Process the Kubernetes configuration - take a copy in case items are added while we're processing
            var ingresses = _store.Ingresses.ToArray();

            return await GetBackendConfiguration(ingresses, cancellation);
        }

        private async Task<BackendConfiguration> GetBackendConfiguration(IEnumerable<IngressModel> ingresses, CancellationToken cancellation)
        {
            var discoveredClusters = new Dictionary<string, Cluster>(StringComparer.Ordinal);
            var discoveredRoutes = new List<ProxyRoute>();
            IEnumerable<ServicePortModel> servicePorts;

            try
            {
                servicePorts = await FindServicesAndPortsAsync(ingresses, cancellation);
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
                    var matchedEndpoint = await _store.GetEndpoint(sp.Namespace, sp.ServiceName, cancellation);
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

            Log.ServiceDiscovered(_logger, discoveredClusters.Count, discoveredRoutes.Count);

            return new BackendConfiguration
            {
                Routes = discoveredRoutes,
                Clusters = discoveredClusters.Values.ToList(),

                // FIXME: Figure out an actual hash for the config
                ConfigurationHash = DateTime.Now.ToString(),
            };
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
                            locatedService = await _store.GetService(ingress.Namespace, path.BackendServiceName, cancellation);
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

        private Cluster BuildCluster(string clusterId, ServicePortModel sp, V1Endpoints endpoints)
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

        private static class Log
        {
            private static readonly Action<ILogger, Exception> _gettingKubernetesApplicationFailed =
                LoggerMessage.Define(
                    LogLevel.Error,
                    EventIds.GettingApplicationFailed,
                    "Could not get applications list from Kubernetes, continuing with zero applications.");

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

            public static void GettingApplicationFailed(ILogger logger, Exception exception)
            {
                _gettingKubernetesApplicationFailed(logger, exception);
            }

            public static void ErrorLoadingEndpoints(ILogger logger, string namespaceName, string serviceName, Exception exception)
            {
                _errorLoadingEndpoints(logger, namespaceName, serviceName, exception);
            }

            public static void ServiceDiscovered(ILogger logger, int discoveredBackendsCount, int discoveredRoutesCount)
            {
                _serviceDiscovered(logger, discoveredBackendsCount, discoveredRoutesCount, null);
            }
        }
    }
}
