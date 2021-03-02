// Copyright (c) 2021 David Bevin
//
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

namespace Bevo.ReverseProxy.Kube
{
    public struct ServiceModel
    {
        public string Namespace { get; set; }

        public string Name { get; set; }
    }
}
