using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Client
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool isConnected = false;
        private ClientObject client;
        private Thread clientThread = null;
        private Task sender = null;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                chatRTB.AppendText($"[ {DateTime.Now:HH:mm} ] {msg}{Environment.NewLine}");
            });
        }
        private void SetIsConnected(bool status)
        {
            isConnected = status;

            Dispatcher.Invoke(() =>
            {
                if (isConnected)
                {
                    addressTB.IsEnabled = false;
                    portTB.IsEnabled = false;
                    usernameTB.IsEnabled = false;
                    passwordTB.IsEnabled = false;
                    sendButton.IsEnabled = true;
                    messageTB.IsEnabled = true;

                    connectButton.Content = "Disconnect";

                    Log("You connected");
                }
                else
                {
                    addressTB.IsEnabled = true;
                    portTB.IsEnabled = true;
                    usernameTB.IsEnabled = true;
                    passwordTB.IsEnabled = true;
                    sendButton.IsEnabled = false;
                    messageTB.IsEnabled = false;

                    connectButton.Content = "Connect";

                    Log("You disconnected");
                }
            });
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (isConnected)
            {
                client.TcpClient.Close();
            }
            else
            {
                CreateConnection();
            }
        }

        private void CreateConnection()
        {
            if (clientThread == null || !clientThread.IsAlive)
            {
                try
                {
                    string address = addressTB.Text.Trim();
                    string number = portTB.Text.Trim();
                    string username = usernameTB.Text.Trim();

                    IPAddress ip = null;
                    try
                    {
                        ip = IPAddress.Parse(address);
                    }
                    catch
                    {
                        throw new Exception("Invalid address");
                    }

                    if (number.Length < 1)
                    {
                        throw new Exception("Missing port");
                    }
                    if (!int.TryParse(number, out int port))
                    {
                        throw new Exception("Invalid port");
                    }
                    if (port < 0 || port > 65535)
                    {
                        throw new Exception("Invalid port");
                    }
                    if (username.Length < 1)
                    {
                        throw new Exception("Missing username");
                    }

                    string pass = string.Empty;
                    Dispatcher.Invoke(() =>
                    {
                        pass = passwordTB.Text;
                    });

                    clientThread = new Thread(() => Connect(ip, port, username, pass))
                    {
                        IsBackground = true
                    };
                    clientThread.Start();
                }
                catch (Exception err)
                {
                    Log(err.Message);
                }
            }
        }
        private void Connect(IPAddress ip, int port, string username, string pass)
        {
            try
            {
                client = new ClientObject
                {
                    Username = username,
                    Pass = pass,
                    TcpClient = new TcpClient(),
                    Data = new StringBuilder(),
                    Handler = new EventWaitHandle(false, EventResetMode.AutoReset)
                };

                client.TcpClient.Connect(ip, port);
                client.Stream = client.TcpClient.GetStream();
                client.Buffer = new byte[client.TcpClient.ReceiveBufferSize];

                if (Authorize())
                {
                    ReceiveMessages();

                    client.TcpClient.Close();
                    SetIsConnected(false);
                }
            }
            catch (Exception err)
            {
                Log(err.Message);
            }
        }

        private void ReceiveMessages()
        {
            while (client.TcpClient.Connected)
            {
                try
                {
                    client.Stream.BeginRead(client.Buffer, 0, client.Buffer.Length, new AsyncCallback(Read), null);
                    client.Handler.WaitOne();
                }
                catch (Exception err)
                {
                    Log(err.Message);
                }
            }
        }

        private void Read(IAsyncResult result)
        {
            int bytes = 0;
            if (client.TcpClient.Connected)
            {
                try
                {
                    bytes = client.Stream.EndRead(result);
                }
                catch (Exception err)
                {
                    Log(err.Message);
                }
            }
            if (bytes > 0)
            {
                client.Data.AppendFormat("{0}", Encoding.UTF8.GetString(client.Buffer, 0, bytes));
                try
                {
                    if (client.Stream.DataAvailable)
                    {
                        client.Stream.BeginRead(client.Buffer, 0, client.Buffer.Length, new AsyncCallback(Read), null);
                    }
                    else
                    {
                        Log(client.Data.ToString());
                    }
                }
                catch (Exception err)
                {
                    Log(err.Message);
                }
                finally
                {
                    client.Data.Clear();
                    client.Handler.Set();
                }
            }
            else
            {
                client.TcpClient.Close();
                client.Handler.Set();
            }
        }

        private bool Authorize()
        {
            Dictionary<string, string> data = new Dictionary<string, string>
            {
                { "username", client.Username },
                { "pass", client.Pass }
            };

            Send(JsonSerializer.Serialize(data));

            try
            {
                client.Stream.BeginRead(client.Buffer, 0, client.Buffer.Length, new AsyncCallback(ReadAuth), null);
                client.Handler.WaitOne();
            }
            catch (Exception err)
            {
                Log(err.Message);
            }
            if (!isConnected)
            {
                Log("Authorisation error");
            }
            return isConnected;
        }

        private void ReadAuth(IAsyncResult result)
        {
            int bytes = 0;
            if (client.TcpClient.Connected)
            {
                try
                {
                    bytes = client.Stream.EndRead(result);
                }
                catch (Exception err)
                {
                    Log(err.Message);
                }
            }
            if (bytes != 0)
            {
                client.Data.AppendFormat("{0}", Encoding.UTF8.GetString(client.Buffer, 0, bytes));
                try
                {
                    if (client.Stream.DataAvailable)
                    {
                        client.Stream.BeginRead(client.Buffer, 0, client.Buffer.Length, new AsyncCallback(ReadAuth), null);
                    }
                    else
                    {
                        Dictionary<string, string> data = JsonSerializer.Deserialize<Dictionary<string, string>>(client.Data.ToString());
                        if (data.ContainsKey("status"))
                        {
                            if (data["status"].Equals("authorized"))
                            {
                                SetIsConnected(true);
                            }
                            else if (data["status"].Equals("wrong password"))
                            {
                                Log("Wrong password");
                            }
                            else if (data["status"].Equals("wrong username"))
                            {
                                Log("The given username is taken");
                            }
                        }
                    }
                }
                catch (Exception err)
                {
                    Log(err.Message);
                }
                finally
                {
                    client.Data.Clear();
                    client.Handler.Set();
                }
            }
            else
            {
                client.TcpClient.Close();
                client.Handler.Set();
            }
        }

        private void Send(string msg)
        {
            if (sender == null || sender.IsCompleted)
            {
                sender = Task.Factory.StartNew(() => Write(msg));
            }
            else
            {
                sender.ContinueWith(a => Write(msg));
            }
        }

        private void Write(string msg)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            if (client.TcpClient.Connected)
            {
                try
                {
                    client.Stream.WriteAsync(buffer, 0, buffer.Length);
                }
                catch (Exception err)
                {
                    Log(err.Message);
                }
            }
        }

        private void SendMessage()
        {
            if (!string.IsNullOrWhiteSpace(messageTB.Text))
            {
                Log($"{client.Username} (You): {messageTB.Text}");

                if (isConnected)
                {
                    Send(messageTB.Text);
                }
                messageTB.Clear();
            }
        }

        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            SendMessage();
        }

        private void MessageTB_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                SendMessage();
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (isConnected)
            {
                client.TcpClient.Close();
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                chatRTB.Document.Blocks.Clear();
            });
        }
    }
}