using Microsoft.Xaml.Behaviors;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace _Project__WPF_SerialCan
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private SerialPort sPort;
        private AutoResetEvent dataAvailable = new AutoResetEvent(false);
        private Queue<byte[]> buffQueue = new Queue<byte[]>();
        private int countBytesToReadLength = 0;
        private DispatcherTimer timer;
        private string trongLuong = "";

        public MainWindow()
        {
            InitializeComponent();
            ContentRendered += MainWindow_ContentRendered;
        }

        private void MainWindow_ContentRendered(object sender, EventArgs e)
        {
            //Get list serial port in commputer
            List<string> sPortNames = new List<string>();
            foreach (var sPortName in SerialPort.GetPortNames())
            {
                sPortNames.Add(sPortName);
            }
            cboSPorts.ItemsSource = sPortNames;

            //Create list reader model
            List<string> readerNames = new List<string>();
            readerNames.Add("W100");
            readerNames.Add("FilletTP");
            readerNames.Add("DIGI28SS");
            cboReaderModels.ItemsSource = readerNames;
            cboReaderModels.SelectedItem = "W100";
            //Create list serial port baudrates
            List<int> baudRates = new List<int>();
            baudRates.Add(300);
            baudRates.Add(600);
            baudRates.Add(1200);
            baudRates.Add(2400);
            baudRates.Add(4800);
            baudRates.Add(9600);
            baudRates.Add(19200);
            baudRates.Add(38400);
            baudRates.Add(57600);
            baudRates.Add(115200);
            baudRates.Add(230400);
            baudRates.Add(460800);
            baudRates.Add(921600);
            cboSerialBaudRate.ItemsSource = baudRates;
            cboSerialBaudRate.SelectedItem = 9600;
            //Preparing serial port info
            sPort = new SerialPort();
            sPort.Parity = Parity.None;
            sPort.DataBits = 8;
            sPort.StopBits = StopBits.One;
            sPort.Handshake = Handshake.None;
            sPort.NewLine = Environment.NewLine;

            //Register Envent
            Closing += MainWindow_Closing;
            btnConnect.Click += BtnConnect_Click;
            btnDisConnect.Click += BtnDisConnect_Click;
            btnClear.Click += BtnClear_Click;
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            rtxtLog.SelectAll();
            rtxtLog.Selection.Text = "";
            lblCountBytes.Content = "0";
            lblMaxCountBytesError.Content = "0";
            txtData.Text = "";
            countBytesToReadLength = 0;
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            BtnDisConnect_Click(sender, new RoutedEventArgs());
        }

        private void BtnDisConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (chkDMA.IsChecked == true)
                {
                    sPort.DataReceived -= SPort_DMA_DataReceived;
                }

                if (chkTimer.IsChecked == true)
                {
                    timer.Stop();
                    timer.Tick -= Timer_Tick;
                }
                sPort.Close();

                rtxtLog.AppendText($"[{DateTime.Now}][DisConnected] {cboSPorts.SelectedValue}{Environment.NewLine}");
                wrpTypeConnection.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                rtxtLog.AppendText($"Disconnected Error: {ex.Message}");
            }
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                sPort.BaudRate = (int)cboSerialBaudRate.SelectedValue;
                sPort.DtrEnable = true;
                sPort.RtsEnable = true;
                sPort.PortName = cboSPorts.SelectedValue.ToString();
                if (chkDMA.IsChecked == true)
                {
                    Task task = new Task(new Action(ProcessData));
                    task.Start();
                    sPort.ReceivedBytesThreshold = 9;
                    sPort.DataReceived += SPort_DMA_DataReceived;
                }

                if (chkTimer.IsChecked == true)
                {
                    timer = new DispatcherTimer();
                    timer.Interval = new TimeSpan(0, 0, 0, 0, int.Parse(txtTimerInterval.Text));
                    if (cboReaderModels.SelectedValue.ToString() == "W100")
                        sPort.ReceivedBytesThreshold = 8;
                    timer.Tick += Timer_Tick;
                    timer.Start();
                }

                sPort.Open();
                if (chkBaseStream.IsChecked == true)
                {
                    ReadEvent();
                }
                wrpTypeConnection.IsEnabled = false;
            }
            catch (Exception ex)
            {
                rtxtLog.AppendText($"Connect Error: {ex.Message} {Environment.NewLine}");
                MessageBox.Show(ex.Message);
            }
        }

        private void ReadEvent()
        {
            byte[] buffer = new byte[2000];
            Action kickoffRead = null;
            kickoffRead = (Action)(() => sPort.BaseStream.BeginRead(buffer, 0, buffer.Length, delegate (IAsyncResult ar)
            {
                try
                {
                    if (sPort.IsOpen == false)
                    {
                        return;
                    }
                    int count = sPort.BaseStream.EndRead(ar);
                    byte[] dst = new byte[count];
                    Buffer.BlockCopy(buffer, 0, dst, 0, count);
                    RaiseAppSerialDataEvent(dst);
                }
                catch (Exception exception)
                {
                    MessageBox.Show("ERROR ==> " + exception.ToString());
                }
                kickoffRead();
            }, null));
            kickoffRead();
        }

        private void RaiseAppSerialDataEvent(byte[] Data)
        {
            string Result = Encoding.ASCII.GetString(Data);
            App.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (chkIsLog.IsChecked == true)
                {
                    rtxtLog.AppendText($"{Result}{Environment.NewLine}");
                }

                if (Result.IndexOf(Environment.NewLine) != -1)
                {
                    trongLuong += Result.Substring(0, Result.IndexOf(Environment.NewLine));
                    if (trongLuong.Length >= 6)
                        txtData.Text = trongLuong.Substring(0, 6);
                    trongLuong = "";
                }
                else
                {
                    trongLuong += Result;
                }
            }));
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                int bytesCount = sPort.BytesToRead;
                if (bytesCount > 0)
                {
                    byte[] buffer = new byte[bytesCount];
                    sPort.Read(buffer, 0, bytesCount);
                    var data = Encoding.ASCII.GetString(buffer).Trim();
                    //if(s.Length == )
                    if (cboReaderModels.SelectedValue.ToString() == "DIGI28SS")
                    {
                        if (data[0] == '=')
                            try
                            {
                                char[] chars = { data[1], data[2], data[3], data[4], data[5], data[6], data[7] };
                                txtData.Text = double.Parse(new string(chars)).ToString(CultureInfo.InvariantCulture);
                                countBytesToReadLength = 0;
                            }
                            catch (Exception ex)
                            {
                                countBytesToReadLength++;
                                rtxtLog.AppendText($"Error: {ex.Message} {Environment.NewLine}");
                            }
                        else
                        {
                            countBytesToReadLength++;
                        }
                    }
                    else if (cboReaderModels.SelectedValue.ToString() == "W100")
                    {
                        try
                        {
                            if (data.Length == 6)
                            {
                                txtData.Text = double.Parse(data).ToString();
                                countBytesToReadLength = 0;
                            }
                            else
                            {
                                countBytesToReadLength++;
                            }
                        }
                        catch (Exception ex)
                        {
                            countBytesToReadLength++;
                            rtxtLog.AppendText($"Error: {ex.Message} {Environment.NewLine}");
                        }
                    }
                    else if (cboReaderModels.SelectedValue.ToString() == "FilletTP")
                    {
                        try
                        {
                            if (data.Length >= 7)
                            {
                                data = data.Replace("w", string.Empty).Replace("n", string.Empty).Replace("k", string.Empty).Replace("g", string.Empty);
                                if (data.Length == 7)
                                {
                                    txtData.Text = double.Parse(data).ToString(CultureInfo.InvariantCulture);
                                    countBytesToReadLength = 0;
                                }
                                else
                                {
                                    countBytesToReadLength++;
                                }
                            }
                            else
                            {
                                countBytesToReadLength++;
                            }
                        }
                        catch (Exception ex)
                        {
                            countBytesToReadLength++;
                            rtxtLog.AppendText($"Error: {ex.Message} {Environment.NewLine}");
                        }
                    }
                    else
                    {
                        txtData.Text = data;
                    }
                    if (chkIsLog.IsChecked == true)
                    {
                        rtxtLog.AppendText($"{data} {Environment.NewLine}");
                    }
                }
                else
                {
                    countBytesToReadLength++;
                }

                if (countBytesToReadLength >= int.Parse(txtErrorCount.Text))
                {
                    txtData.Text = "error";
                }
                lblCountBytes.Content = countBytesToReadLength.ToString();
                if (countBytesToReadLength >= int.Parse(lblMaxCountBytesError.Content.ToString()))
                {
                    lblMaxCountBytesError.Content = countBytesToReadLength.ToString();
                }
            }
            catch (Exception ex)
            {
                rtxtLog.AppendText($"Error: {ex.Message} {Environment.NewLine}");
            }
        }

        private void SPort_DMA_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            while (sPort.BytesToRead > 0)
            {
                byte[] chunk = new byte[sPort.BytesToRead];
                sPort.Read(chunk, 0, chunk.Length);
                lock (buffQueue)
                    buffQueue.Enqueue(chunk);
                dataAvailable.Set();
            }
        }

        private void ProcessData()
        {
            while (true)
            {
                dataAvailable.WaitOne();
                while (buffQueue.Count > 0)
                {
                    byte[] chunk;
                    lock (buffQueue)
                    {
                        chunk = buffQueue.Dequeue();
                    }

                    var data = Encoding.ASCII.GetString(chunk).Trim();
                    App.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (cboReaderModels.SelectedValue.ToString() == "DIGI28SS")
                        {
                            if (data.Length > 0 && data[0] == '=')
                                try
                                {
                                    char[] chars = { data[1], data[2], data[3], data[4], data[5], data[6], data[7] };
                                    txtData.Text = new string(chars);
                                    countBytesToReadLength = 0;
                                }
                                catch (Exception ex)
                                {
                                    countBytesToReadLength++;
                                    rtxtLog.AppendText($"Error: {ex.Message} {Environment.NewLine}");
                                }
                            else
                            {
                                countBytesToReadLength++;
                            }
                        }
                        else if (cboReaderModels.SelectedValue.ToString() == "W100")
                        {
                            try
                            {
                                if (data.Length == 6)
                                {
                                    char[] chars = { data[0], data[1], data[2], data[3], data[4], data[5] };
                                    txtData.Text = new string(chars);
                                    //txtData.Text = double.Parse(data).ToString(CultureInfo.InvariantCulture);
                                    countBytesToReadLength = 0;
                                }
                                else
                                {
                                    countBytesToReadLength++;
                                }
                            }
                            catch (Exception ex)
                            {
                                countBytesToReadLength++;
                                rtxtLog.AppendText($"Error: {ex.Message} {Environment.NewLine}");
                            }
                        }
                        else if (cboReaderModels.SelectedValue.ToString() == "FilletTP")
                        {
                            try
                            {
                                if (data.Length >= 7)
                                {
                                    data = data.Replace("w", string.Empty).Replace("n", string.Empty).Replace("k", string.Empty).Replace("g", string.Empty);
                                    if (data.Length == 7)
                                    {
                                        txtData.Text = double.Parse(data).ToString(CultureInfo.InvariantCulture);
                                        countBytesToReadLength = 0;
                                    }
                                    else
                                    {
                                        countBytesToReadLength++;
                                    }
                                }
                                else
                                {
                                    countBytesToReadLength++;
                                }
                            }
                            catch (Exception ex)
                            {
                                countBytesToReadLength++;
                                rtxtLog.AppendText($"Error: {ex.Message} {Environment.NewLine}");
                            }
                        }
                        else
                        {
                            txtData.Text = data;
                        }

                        if (chkIsLog.IsChecked == true)
                        {
                            rtxtLog.AppendText($"{data} {Environment.NewLine}");
                        }

                        if (countBytesToReadLength >= int.Parse(txtErrorCount.Text))
                        {
                            txtData.Text = "error";
                        }
                        lblCountBytes.Content = countBytesToReadLength.ToString();
                        if (countBytesToReadLength >= int.Parse(lblMaxCountBytesError.Content.ToString()))
                        {
                            lblMaxCountBytesError.Content = countBytesToReadLength.ToString();
                        }
                    }));
                }
            }
        }
    }

    public class ScrollToBottomAction : TriggerAction<RichTextBox>
    {
        protected override void Invoke(object parameter)
        {
            AssociatedObject.ScrollToEnd();
        }
    }
}