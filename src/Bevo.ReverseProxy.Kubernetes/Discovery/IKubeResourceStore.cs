// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;

namespace Bevo.ReverseProxy.Kube
{
    public interface IKubeResourceStore
    {
        IEnumerable<IngressModel> Ingresses { get; }

        IChangeToken ChangeToken { get; }

        Task<BackendConfiguration> GetBackendConfiguration(IEnumerable<IngressModel> ingresses, CancellationToken cancellation);
    }
}
