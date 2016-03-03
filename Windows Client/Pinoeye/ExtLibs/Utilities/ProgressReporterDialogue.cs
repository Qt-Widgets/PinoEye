using System;
using System.ComponentModel;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// Form that is shown to the user during a background operation
    /// </summary>
    /// <remarks>
    /// Performs operation excplicitely on a threadpool thread due to 
    /// Mono not playing nice with the BackgroundWorker
    /// </remarks>
    public partial class ProgressReporterDialogue : Form
    {
        private Exception workerException;
        public ProgressWorkerEventArgs doWorkArgs;

        internal object locker = new object();
        internal int _progress = -1;
        internal string _status = "";

        public bool Running = false;

        public delegate void DoWorkEventHandler(object sender, ProgressWorkerEventArgs e, object passdata = null);

        // This is the event that will be raised on the BG thread
        public event DoWorkEventHandler DoWork;

        public ProgressReporterDialogue()
        {
            doWorkArgs = new ProgressWorkerEventArgs();
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;

        }

        /// <summary>
        /// Called at setup - will kick off the background process on a thread pool thread
        /// </summary>
        public void RunBackgroundOperationAsync()
        {
            ThreadPool.QueueUserWorkItem(RunBackgroundOperation);
            this.ShowDialog();
        }

        private void RunBackgroundOperation(object o)
        {
            Running = true;

            try
            {
                Thread.CurrentThread.Name = "ProgressReporterDialogue Background thread";
            }
            catch { } // ok on windows - fails on mono

            // mono fix - ensure the dialog is running
            while (this.IsHandleCreated == false)
            {
                System.Threading.Thread.Sleep(100);
            }

            this.Invoke((MethodInvoker)delegate
         {
             // if this windows isnt the current active windows, popups inherit the wrong parent.
             this.Focus();
             Application.DoEvents();
         });

            try
            {
                if (this.DoWork != null) this.DoWork(this, doWorkArgs);

            }
            catch(Exception e)
            {
                // The background operation thew an exception.
                // Examine the work args, if there is an error, then display that and the exception details
                // Otherwise display 'Unexpected error' and exception details
                ShowDoneWithError(e, doWorkArgs.ErrorMessage);
                Running = false;
                return;
            }

            // stop the timer

            // run once more to do final message and progressbar
            if (this.IsDisposed || this.Disposing || !this.IsHandleCreated)
            {
                return;
            }

            if (doWorkArgs.CancelRequested && doWorkArgs.CancelAcknowledged)
            {
                Running = false;
                return;
            }

            if (!string.IsNullOrEmpty(doWorkArgs.ErrorMessage))
            {
                ShowDoneWithError(null, doWorkArgs.ErrorMessage);
                Running = false;
                return;
            }

            if (doWorkArgs.CancelRequested)
            {
                ShowDoneWithError(null, "Operation could not cancel");
                Running = false;
                return;
            }

            ShowDone();
            Running = false;
        }

        // Called as a possible last operation of the bg thread that was cancelled
        // - Hide progress bar 
        // - Set label text


        // Called as a possible last operation of the bg thread
        // - Set progress bar to 100%
        // - Wait a little bit to allow the Aero progress animatiom to catch up
        // - Signal that we can close
        private void ShowDone()
        {
            Thread.Sleep(1000);

            this.BeginInvoke((MethodInvoker) this.Close);
        }

        // Called as a possible last operation of the bg thread
        // There was an exception on the worker event, so:
        // - Show the error message supplied by the worker, or a default message
        // - Make visible the error icon
        // - Make the progress bar invisible to make room for:
        // - Add the exception details and stack trace in an expansion panel
        // - Change the Cancel button to 'Close', so that the user can look at the exception message a bit
        private void ShowDoneWithError(Exception exception, string doWorkArgs)
        {
            var errMessage = doWorkArgs ?? "There was an unexpected error";

            if (this.Disposing || this.IsDisposed)
                return;
        }

        /// <summary>
        /// Called from the BG thread
        /// </summary>
        /// <param name="progress">progress in %, -1 means inderteminate</param>
        /// <param name="status"></param>
        public void UpdateProgressAndStatus(int progress, string status)
        {
            // we don't let the worker update progress when  a cancel has been
            // requested, unless the cancel has been acknowleged, so we know that
            // this progress update pertains to the cancellation cleanup
            if (doWorkArgs.CancelRequested && !doWorkArgs.CancelAcknowledged)
                return;

            lock (locker)
            {
                _progress = progress;
                _status = status;
            }

        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var message = this.workerException.Message
                          + Environment.NewLine + Environment.NewLine
                          + this.workerException.StackTrace;

          
        }

        /// <summary>
        /// prevent using invokes on main update status call "UpdateProgressAndStatus", as this is slow on mono
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
 

        private void ProgressReporterDialogue_Load(object sender, EventArgs e)
        {
            this.Focus();
        }

    }

    public class ProgressWorkerEventArgs : EventArgs
    {
        public string ErrorMessage;
        public volatile bool CancelRequested;
        public volatile bool CancelAcknowledged;
    }
}
