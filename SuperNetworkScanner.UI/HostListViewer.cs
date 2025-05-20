using System.Data;

namespace SuperNetworkScanner.UI
{
    public partial class HostListViewer : Form
    {
        public HostListViewer()
        {
            InitializeComponent();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            //RefreshMap();
        }

        private void RefreshMap()
        {
            treeView1.Nodes.Clear();
            var hosts = NetworkMap.Hosts.OrderBy(x => x.ToString()).ToList(); //ToList makes a copy
            foreach (var host in hosts)
            {
                var node = treeView1.Nodes.Add(host.ToString());
                var statusColor = Color.Black;
                if (host.Status == Models.HostStatus.Online) statusColor = Color.Green;
                if (host.Status == Models.HostStatus.Offline) statusColor = Color.Red;

                node.ForeColor = statusColor;
                var networkNode = node.Nodes.Add("Network");
                var i = 0;
                foreach (var netInterface in host.NetworkInterfaces)
                {
                    var interfaceNode = networkNode.Nodes.Add($"[{i++}] {netInterface.Name}");
                    interfaceNode.Nodes.Add($"Name - {netInterface.Name}");
                    interfaceNode.Nodes.Add($"MAC - {netInterface.MAC}");
                    foreach (var ip in netInterface.Ip_Address)
                        interfaceNode.Nodes.Add($"IP - {ip}");
                    var servicesNode = interfaceNode.Nodes.Add("Services");
                    foreach (var service in netInterface.Services)
                    {
                        var serviceNode = servicesNode.Nodes.Add(service.ServiceName);
                        serviceNode.Nodes.Add($"Port - {service.Port}");
                        serviceNode.Nodes.Add($"Protocol - {service.Protocol}");
                        serviceNode.Nodes.Add($"Description - {service.Description}");

                    }

                }
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            RefreshMap();
        }

        private void HostListViewer_Load(object sender, EventArgs e)
        {

        }
        private bool _isExpanded = false; // Tracks the current state of the TreeView

        private void treeView1_DoubleClick(object sender, EventArgs e)
        {
            if (_isExpanded)
            {
                // If currently expanded, collapse all
                treeView1.CollapseAll();
                _isExpanded = false;
               
            }
            else
            {
                // If currently collapsed, expand all
                treeView1.ExpandAll();
                _isExpanded = true;
                
            }
        }
    }
}
