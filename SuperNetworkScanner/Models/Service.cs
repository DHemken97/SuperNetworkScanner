namespace SuperNetworkScanner.Models
{
    public class Service
    {
        public int Port { get; set; }
        public string Description { get; set; }
        public string ServiceName { get; set; }
        public string Protocol { get; set; }
    }

    public class ServiceComparer : IEqualityComparer<Service>
    {
        public bool Equals(Service x, Service y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null) || ReferenceEquals(y, null)) return false;
            return x.Port == y.Port && x.Protocol == y.Protocol;
        }

        public int GetHashCode(Service obj)
        {
            return (obj.Port, obj.Protocol).GetHashCode();
        }
    }
}
