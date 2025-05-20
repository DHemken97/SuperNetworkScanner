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
            };
            CollectionSteps.Add(new FinishedStep());
            IncrementStep();

            search_ips = Enumerable.Range(1, 254).Select(i => $"192.168.12.{i}").ToList();

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
