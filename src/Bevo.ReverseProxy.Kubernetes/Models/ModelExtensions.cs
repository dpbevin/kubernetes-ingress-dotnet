
using System;
using System.Collections.Generic;
using System.Linq;
using k8s.Models;

namespace Bevo.ReverseProxy.Kube
{
    public static class ModelExtensions
    {
        public static IngressModel ToModel(this Extensionsv1beta1Ingress ingress)
        {
            return new IngressModel
            {
                Name = ingress.Metadata.Name,
                Namespace = ingress.Metadata.NamespaceProperty,
                Rules = ingress.Spec.Rules.Select(r => r.ToModel()),
            };
        }

        public static IngressRule ToModel(this Extensionsv1beta1IngressRule rule)
        {
            return new IngressRule
            {
                Host = rule.Host,
                Paths = rule.Http.Paths.Select(p => p.ToModel()),
            };
        }

        public static IngressPath ToModel(this Extensionsv1beta1HTTPIngressPath path)
        {
            return new IngressPath
            {
                Path = path.Path,
                PathType = ParsePathType(path.PathType),
                BackendServiceName = path.Backend?.ServiceName,
                BackendServicePort = path.Backend?.ServicePort,
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
