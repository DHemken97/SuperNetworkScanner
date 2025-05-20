using SuperNetworkScanner.CollectionSteps;

namespace SuperNetworkScanner.UI
{
    public partial class StepViewer : Form
    {
        private HostListViewer hostViewer;
        private List<string> search_ips;

        public List<ICollectionStep> CollectionSteps { get; }
        public ICollectionStep CurrentStep { get; private set; }
        public int StepIndex { get; set; }
        public StepViewer()
        {
            InitializeComponent();
            hostViewer = new HostListViewer();
            hostViewer.Show();

            StepIndex = 0;
            CollectionSteps = new List<ICollectionStep>
            {
                new LocalARPTableStep(),
                new PingSweepStep(),
                 new DNSQueryStep(),
               //  new PortScanStep(){Ports = PortScanStep.KnownPortServices.Select(x => x.Key).ToList()},
                // new PortScanStep(){Ports = new List<int>{ 22,80,443,3389, 135 }  },
                 new PortScanStep(){Ports = new List<int>{ 80,443 }  },
                 new HttpInfoCollectionStep()
            };
            CollectionSteps.Add(new FinishedStep());
            IncrementStep();


        }

        private void IncrementStep()
        {
            if (StepIndex >= CollectionSteps.Count) return;
            StepIndex++;
            CurrentStep = CollectionSteps[StepIndex - 1];
            lblStep.Text = $"Step {StepIndex} - {CurrentStep.Name}";
            lblDescription.Text = CurrentStep.Description;


        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            txtSearchRanges.Enabled = false;
            if (search_ips == null)
            {
                search_ips = new List<string>();
                var ranges = txtSearchRanges.Text.Split(',');
                foreach (var range in ranges)
                {
                    if (range.Contains(".x"))
                    {
                        search_ips.AddRange(Enumerable.Range(1, 254).Select(i => range.Replace("x", i.ToString())).ToList());
                    }
                    else if (range.Contains("-")) // Handles IP ranges like 192.168.1.1-25
                    {
                        var parts = range.Split('-');
                        var startIp = parts[0];
                        var endSuffix = int.Parse(parts[1]);

                        var lastDotIndex = startIp.LastIndexOf('.');
                        if (lastDotIndex != -1)
                        {
                            var prefix = startIp.Substring(0, lastDotIndex + 1); // e.g., "192.168.1."
                            var startSuffix = int.Parse(startIp.Substring(lastDotIndex + 1)); // e.g., "1"

                            for (int i = startSuffix; i < endSuffix; i++)
                            {
                                search_ips.Add(prefix + i.ToString());
                            }
                        }
                    }
                    else // Handles single IPs like 192.168.1.25
                    {
                        search_ips.Add(range);
                    }
                }
            }

                if (btnNext.Text == "Start")
            {
                Task.Run(() => CurrentStep.Start(search_ips));
                btnSkip.Enabled = false;
                btnNext.Enabled = false;
                timer1.Start();
                hostViewer.timer1.Start();

            }
            else
            {
                btnNext.Enabled = true;
                btnNext.Text = "Start";
                btnSkip.Enabled = true;
                IncrementStep();
            }

        }

        private void btnSkip_Click(object sender, EventArgs e)
        {
            IncrementStep();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            progressBar1.Value = (int)(CurrentStep.ProgressPercentage * 100);

            if (CurrentStep.IsCompleted)
            {
                btnNext.Enabled = true;
                btnNext.Text = "Next";
                timer1.Stop();
                hostViewer.timer1.Stop();
                progressBar1.Value = 100;

            }
            lblStep.Text = $"Step {StepIndex} - {CurrentStep?.Name}";
            lblDescription.Text = CurrentStep?.Description;
            richTextBox1.Text = CurrentStep?.ProgressLog;
            lblProgressText.Text = CurrentStep?.ProgressMessage;


        }

        private void StepViewer_Load(object sender, EventArgs e)
        {

        }
    }
}
