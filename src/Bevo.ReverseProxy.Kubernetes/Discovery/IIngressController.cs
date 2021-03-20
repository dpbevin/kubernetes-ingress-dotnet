// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Primitives;

namespace Bevo.ReverseProxy.Kube
{
    public interface IIngressController
    {
        IChangeToken ChangeToken { get; }

        Task<BackendConfiguration> GetConfiguration(CancellationToken cancellation);
    }
}