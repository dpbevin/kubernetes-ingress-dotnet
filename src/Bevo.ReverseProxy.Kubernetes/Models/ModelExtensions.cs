// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System.Globalization;
using System.Linq;
using k8s.Models;

namespace Bevo.ReverseProxy.Kube
{
    public static class ModelExtensions
    {
        public static IngressModel ToModel(this V1Ingress ingress)
        {
            return new IngressModel
            {
                Name = ingress.Metadata.Name,
                Namespace = ingress.Metadata.NamespaceProperty,
                Rules = ingress.Spec.Rules.Select(r => r.ToModel()),
                Original = ingress,
            };
        }

        public static string LegacyIngressClass(this V1Ingress ingress)
        {
            if (ingress != null && ingress.Metadata.Annotations.TryGetValue("kubernetes.io/ingress.class", out var legacyIngressClass))
            {
                return legacyIngressClass;
            }

            return null;
        }

        public static IngressRule ToModel(this V1IngressRule rule)
        {
            return new IngressRule
            {
                Host = rule.Host,
                Paths = rule.Http.Paths.Select(p => p.ToModel()),
            };
        }

        public static IngressPath ToModel(this V1HTTPIngressPath path)
        {
            return new IngressPath
            {
                Path = path.Path,
                PathType = ParsePathType(path.PathType),
                BackendServiceName = path.Backend?.Service.Name,
                BackendServicePort = path.Backend?.Service.Port.Number?.ToString("F", CultureInfo.InvariantCulture),
            };
        }

        public static ServicePortModel ToModel(this V1ServicePort port, string serviceName, string serviceNamespace)
        {
            return new ServicePortModel
            {
                ServiceName = serviceName,
                Namespace = serviceNamespace,
                PortName = port.Name,
                Protocol = port.Protocol,
                Port = port.Port
            };
        }

        public static IngressPath.IngressPathType ParsePathType(string pathType)
        {
            switch (pathType.ToUpperInvariant())
            {
                case "EXACT":
                    return IngressPath.IngressPathType.Exact;

                case "PREFIX":
                    return IngressPath.IngressPathType.Prefix;

                default:
                    // TODO Log error if something unexpected.
                    return IngressPath.IngressPathType.ImplementationSpecific;
            }
        }
    }
}
