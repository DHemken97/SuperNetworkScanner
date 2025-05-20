namespace SuperNetworkScanner.Models
{
    public class Host
    {
        public string Hostname { get; set; }
        public HostStatus Status { get; set; }
        public List<NetworkInterface> NetworkInterfaces { get; set; }



        public override string ToString()
        {
            var firstIp = NetworkInterfaces?.SelectMany(x => x.Ip_Address).Where(x => !string.IsNullOrWhiteSpace(x))?.FirstOrDefault();
            var firstMAC = NetworkInterfaces?.Select(x => x.MAC).Where(x => !string.IsNullOrWhiteSpace(x))?.FirstOrDefault();

            if (string.IsNullOrWhiteSpace(Hostname))
                if (string.IsNullOrWhiteSpace(firstIp))
                    return firstMAC;
                else
                    return firstIp;
            else
                if (string.IsNullOrWhiteSpace(firstIp))
                   return Hostname;
            else
                   return $"{Hostname} ({firstIp})";
        }
    }
}
