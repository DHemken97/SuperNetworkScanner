using System.Net.NetworkInformation;

namespace SuperNetworkScanner.Models
{
    public class NetworkInterface
    {
        public string Name { get; set; }
        public string MAC { get; set; }
        public List<string> Ip_Address { get; set; } = new List<string>();
        public List<Service> Services { get; set; } = new List<Service>();



        public override string ToString()
        {
            var firstIp = this.Ip_Address.Where(x => !string.IsNullOrWhiteSpace(x))?.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(Name))
                if (string.IsNullOrWhiteSpace(firstIp))
                    return MAC;
                else
                    return firstIp;
            else
                if (string.IsNullOrWhiteSpace(firstIp))
                return Name;
            else
                return $"{Name} ({firstIp})";
        }

    }
}
