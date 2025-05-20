using SuperNetworkScanner.CollectionSteps;

namespace SuperNetworkScanner.UI
{
    public partial class StepViewer : Form
    {
        private HostListViewer hostViewer;

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
                new PingSweepStep(),
                new DNSQueryStep(),
            };
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
            if (btnNext.Text == "Start")
            {
                Task.Run(() => CurrentStep.Start());
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
            if (CurrentStep.IsCompleted)
            {
                btnNext.Enabled = true;
                btnNext.Text = "Next";
                timer1.Stop();
                hostViewer.timer1.Stop();

            }
            lblStep.Text = $"Step {StepIndex} - {CurrentStep?.Name}";
            lblDescription.Text = CurrentStep?.Description;
            progressBar1.Value = (int)(CurrentStep.ProgressPercentage*100);
            richTextBox1.Text = CurrentStep?.ProgressLog;
            lblProgressText.Text = CurrentStep?.ProgressMessage;


        }
    }
}
