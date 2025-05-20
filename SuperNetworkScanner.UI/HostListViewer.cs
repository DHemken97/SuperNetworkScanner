using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

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
            var hosts = NetworkMap.Hosts.ToList(); //ToList makes a copy
            foreach (var host in hosts)
            {
                var hostname = host.Hostname;
                var IPs = host.NetworkInterfaces?.SelectMany(x => x.Ip_Address).Where(x => !string.IsNullOrEmpty(x));
                var firstIp = IPs?.FirstOrDefault();
                var multiple = IPs?.Count() > 1 ? $" + {IPs.Count() - 1} more" : "";
                if (string.IsNullOrWhiteSpace(hostname)) hostname = firstIp;
                var node = treeView1.Nodes.Add($"{hostname} ({firstIp}{multiple})");
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
    }
}
