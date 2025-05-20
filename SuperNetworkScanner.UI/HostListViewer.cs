using System.Collections;
using System.Data;
using System.Reflection;
using SuperNetworkScanner.Models;

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
                AddHostDynamically(host);


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



        private void AddHostDynamically(Host host)
        {
            if (host == null)
            {
                treeView1.Nodes.Add("Error: Host object is null.");
                return;
            }

            // You might want to clear existing nodes if you only want to display the new host
            // treeView1.Nodes.Clear();

            // The root node for the Host object, using its IP Address for the name
            // Or you could use host.ToString() if preferred for the very top level
            var rootNode = treeView1.Nodes.Add(host.ToString());
            if (host.Status == HostStatus.Offline) rootNode.ForeColor = Color.Red;
            if (host.Status == HostStatus.Online) rootNode.ForeColor = Color.Green;

            // Recursively add properties of the Host object
            // Pass the root node itself and the "Host" as the property name for the initial call
            AddObjectPropertiesAsNodes(host, rootNode);
        }

        /// <summary>
        /// Recursively adds properties of an object to a TreeNode.
        /// If a property holds a complex object or a collection, the node is named after the property name,
        /// and its content is added as sub-nodes.
        /// If a property holds a simple value type or string, the node is named "PropertyName - Value".
        /// </summary>
        /// <param name="obj">The object whose properties are to be added.</param>
        /// <param name="parentNode">The TreeNode to which the properties will be added as sub-nodes.</param>
        private void AddObjectPropertiesAsNodes(object obj, TreeNode parentNode)
        {
            if (obj == null) return;

            PropertyInfo[] properties = obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (PropertyInfo prop in properties)
            {
                if (prop.Name == "ProgressLog" || prop.Name == "ProgressMessage" || prop.Name == "IsCompleted" ||
                    prop.Name == "Item")
                {
                    continue;
                }

                object value = null;
                try
                {
                    value = prop.GetValue(obj);
                }
                catch (TargetParameterCountException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    parentNode.Nodes.Add($"{prop.Name} - Error: {ex.Message}");
                    continue;
                }

                if (value == null)
                {
                    parentNode.Nodes.Add($"{prop.Name} - Null");
                    continue;
                }

                IEnumerable enumerableValue = value as IEnumerable;
                bool isCollection = enumerableValue != null && !(value is string);

                bool isComplexObject = prop.PropertyType.IsClass && prop.PropertyType != typeof(string) && !isCollection;

                if (isCollection)
                {
                    TreeNode collectionNode = parentNode.Nodes.Add(prop.Name);

                    if (!enumerableValue.Cast<object>().Any())
                    {
                        collectionNode.Nodes.Add("(Empty)");
                    }
                    else
                    {
                        foreach (object item in enumerableValue)
                        {
                            if (item == null)
                            {
                                collectionNode.Nodes.Add("Null Item");
                                continue;
                            }

                            TreeNode itemNode = collectionNode.Nodes.Add(item.ToString());

                            if (item.GetType().IsClass && item.GetType() != typeof(string))
                            {
                                AddObjectPropertiesAsNodes(item, itemNode);
                            }
                        }
                    }
                }
                else if (isComplexObject)
                {
                    TreeNode objNode = parentNode.Nodes.Add(prop.Name);
                    AddObjectPropertiesAsNodes(value, objNode);
                }
                else
                {
                    parentNode.Nodes.Add($"{prop.Name} - {value.ToString()}");
                }
            }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {

        }
    }
}
