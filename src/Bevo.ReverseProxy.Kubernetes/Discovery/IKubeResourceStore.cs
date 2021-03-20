// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using k8s.Models;
using Microsoft.Extensions.Primitives;

namespace Bevo.ReverseProxy.Kube
{
    public interface IKubeResourceStore
    {
        IChangeToken ChangeToken { get; }

        IEnumerable<IngressModel> Ingresses { get; }

        Task<V1Endpoints> GetEndpoint(string namespaceName, string serviceName, CancellationToken cancellation);

        Task<V1Service> GetService(string namespaceName, string serviceName, CancellationToken cancellation);
    }
}
