// Copyright (c) 2021 David Bevin
// 
// This software is released under the MIT License.
// https://opensource.org/licenses/MIT

namespace Bevo.ReverseProxy.Kube
{
    public struct IngressPath
    {
        public string Path { get; set; }

        public IngressPathType PathType { get; set; }

        public string BackendServiceName { get; set; }

        public string BackendServicePort { get; set; }

        public enum IngressPathType
        {
            ImplementationSpecific,
            Exact,
            Prefix
        }
    }
}
