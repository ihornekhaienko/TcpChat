using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Server
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool isActive = false;
        private long id = 0;

        TcpListener tcpListener;
        private Thread listener = null;
        private Task sender = null;
        private Thread disconnector = null;

        private ConcurrentDictionary<long, ClientObject> clients = null;

        public MainWindow()
        {
            clients = new ConcurrentDictionary<long, ClientObject>();
            InitializeComponent();
        }

        private void Log(string msg)
        {
            Dispatcher.Invoke(() =>
            {
                chatRTB.AppendText($"[ {DateTime.Now:HH:mm} ] {msg}{Environment.NewLine}");
            });
        }

        private void SetIsActive(bool status)
        {
            isActive = status;
            Dispatcher.Invoke(() =>
            {
                if (isActive)
                {
                    addressTB.IsEnabled = false;
                    portTB.IsEnabled = false;
                    usernameTB.IsEnabled = false;
                    passwordTB.IsEnabled = false;
                    sendButton.IsEnabled = true;
                    messageTB.IsEnabled = true;

                    startButton.Content = "Stop";

                    Log("The server is running. Waiting for connections...");
                }
                else
                {
                    addressTB.IsEnabled = true;
                    portTB.IsEnabled = true;
                    usernameTB.IsEnabled = true;
                    passwordTB.IsEnabled = true;
                    sendButton.IsEnabled = false;
                    messageTB.IsEnabled = false;

                    startButton.Content = "Start";

                    Log("Server stopped");
                }
            });
        }

        private void  StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isActive)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }

        private void Start()
        {
            if (listener == null || !listener.IsAlive)
            {
                try
                {
                    string address = addressTB.Text.Trim();
                    string number = portTB.Text.Trim();
                    string username = usernameTB.Text.Trim();

                    IPAddress ip;
                    try
                    {
                        ip = IPAddress.Parse(address);
                    }
                    catch
                    {
                        throw new Exception("Invalid address");
                    }
                    if (string.IsNullOrWhiteSpace(number))
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
                    if (string.IsNullOrWhiteSpace(username))
                    {
                        throw new Exception("Missing username");
                    }

                    listener = new Thread(() => Listen(ip, port))
                    {
                        IsBackground = true
                    };
                    listener.Start();
                }
                catch (Exception err)
                {
                    Log(err.Message);
                }
            }
        }

        private void Stop()
        {
            isActive = false;
            tcpListener.Stop();
            DisconnectAll();
        }

        private void Listen(IPAddress ip, int port)
        {
            tcpListener = null;
            SetIsActive(true);

            try
            {
                tcpListener = new TcpListener(ip, port);
                tcpListener.Start();

                while (isActive)
                {
                    if (tcpListener.Pending())
                    {
                        try
                        {
                            ClientObject client = new ClientObject
                            {
                                Id = id++,
                                Username = string.Empty,
                                TcpClient = tcpListener.AcceptTcpClient(),
                                Data = new StringBuilder(),
                                Handler = new EventWaitHandle(false, EventResetMode.AutoReset)
                            };

                            client.Stream = client.TcpClient.GetStream();
                            client.Buffer = new byte[client.TcpClient.ReceiveBufferSize];

                            Thread connector = new Thread(() => Connect(client))
                            {
                                IsBackground = true
                            };
                            connector.Start();
                        }
                        catch (Exception err)
                        {
                            Log(err.Message);
                        }
                    }
                    else
                    {
                        Thread.Sleep(500);
                    }
                }
            }
            catch (Exception err)
            {
                Log(err.Message);
            }
            finally
            {
                SetIsActive(false);

                if (tcpListener != null)
                {
                    tcpListener.Server.Close();
                }
            }
        }

        private bool Authorize(ClientObject client)
        {
            try
            {
                client.Stream.BeginRead(client.Buffer, 0, client.Buffer.Length, new AsyncCallback(EndReadAuth), client);
                client.Handler.WaitOne();
            }
            catch (Exception err)
            {
                Log(err.Message);
            }
            return !string.IsNullOrWhiteSpace(client.Username);
        }

        private void EndReadAuth(IAsyncResult result)
        {
            ClientObject client = result.AsyncState as ClientObject;
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
                        client.Stream.BeginRead(client.Buffer, 0, client.Buffer.Length, new AsyncCallback(EndReadAuth), client);
                    }
                    else
                    {
                        Dictionary<string, string> data = JsonSerializer.Deserialize<Dictionary<string, string>>(client.Data.ToString());

                        string pass = string.Empty;
                        Dispatcher.Invoke(() =>
                        {
                            pass = passwordTB.Text;
                        });
                        string host = string.Empty;
                        Dispatcher.Invoke(() =>
                        {
                            host = usernameTB.Text;
                        });

                        if (!data["pass"].Equals(pass))
                        {
                            SendToOne("{\"status\": \"wrong password\"}", client);
                            Thread.Sleep(100);
                            client.TcpClient.Close();
                        }
                        else if (clients.Values.Any(c => data["username"].Equals(c.Username)) || data["username"].Equals(host))
                        {
                            SendToOne("{\"status\": \"wrong username\"}", client);
                            Thread.Sleep(100);
                            client.TcpClient.Close();
                        }
                        else
                        {
                            client.Username += data["username"];
                            SendToOne("{\"status\": \"authorized\"}", client);
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

        private void Connect(ClientObject client)
        {

            if (Authorize(client))
            {
                clients.TryAdd(client.Id, client);
                AddToGrid(client.Id, client);
                Log($"{client.Username} connected");
                SendToAll($"{client.Username} connected", client.Id);

                ReceiveMessages(client);

                client.TcpClient.Close();

                clients.TryRemove(client.Id, out ClientObject temp);
                RemoveFromGrid(temp.Id);
                Log($"{temp.Username} disconnected");
                SendToAll($"{temp.Username} disconnected", temp.Id);
            }
        }

        private void ReceiveMessages(ClientObject client)
        {
            while (client.TcpClient.Connected)
            {
                try
                {
                    client.Stream.BeginRead(client.Buffer, 0, client.Buffer.Length, new AsyncCallback(Read), client);
                    client.Handler.WaitOne();
                }
                catch (Exception err)
                {
                    Log(err.Message);
                }
            }
        }

        private void SendToOne(string msg, ClientObject obj)
        {
            if (sender == null || sender.IsCompleted)
            {
                sender = Task.Factory.StartNew(() => Write(msg, obj));
            }
            else
            {
                sender.ContinueWith(antecendent => Write(msg, obj));
            }
        }

        private void SendToAll(string msg, long id = -1)
        {
            if (sender == null || sender.IsCompleted)
            {
                sender = Task.Factory.StartNew(() => Write(msg, id));
            }
            else
            {
                sender.ContinueWith(antecendent => Write(msg, id));
            }
        }

        private void Write(string msg, ClientObject client)
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

        private void Write(string msg, long id = -1)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(msg);
            foreach (var c in clients.Values)
            {
                if (id != c.Id && c.TcpClient.Connected)
                {
                    try
                    {
                        c.Stream.WriteAsync(buffer, 0, buffer.Length);
                    }
                    catch (Exception err)
                    {
                        Log(err.Message);
                    }
                }
            }
        }

        private void Read(IAsyncResult result)
        {
            ClientObject client = result.AsyncState as ClientObject;
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
                        client.Stream.BeginRead(client.Buffer, 0, client.Buffer.Length, new AsyncCallback(Read), client);
                    }
                    else
                    {
                        Log($"{client.Username}: {client.Data}");
                        SendToAll($"{client.Username}: {client.Data}", client.Id);
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

        private void SendMessage()
        {
            if (!string.IsNullOrWhiteSpace(messageTB.Text))
            {
                Log($"{usernameTB.Text.Trim()} (You): {messageTB.Text}");
                SendToAll($"{usernameTB.Text.Trim()}: {messageTB.Text}");

                messageTB.Text = string.Empty;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            isActive = false;
            DisconnectAll();
        }


        private void Disconnect(long id)
        {
            if (disconnector == null || !disconnector.IsAlive)
            {
                disconnector = new Thread(() =>
                {
                    clients.TryGetValue(id, out ClientObject obj);
                    obj.TcpClient.Close();
                    RemoveFromGrid(obj.Id);
                })
                {
                    IsBackground = true
                };
                disconnector.Start();
            }
        }

        public void DisconnectAll()
        {
            if (disconnector == null || !disconnector.IsAlive)
            {
                disconnector = new Thread(() =>
                {
                    foreach (var c in clients.Values)
                    {
                        c.TcpClient.Close();
                    }
                })
                {
                    IsBackground = true
                };
                disconnector.Start();

                Dispatcher.Invoke(() =>
                {
                    clientsDG.Items.Clear();
                });
            }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                chatRTB.Document.Blocks.Clear();
            });
        }

        private void KickButton_Click(object sender, RoutedEventArgs e)
        {
            ClientModel client = (ClientModel)clientsDG.SelectedItem;

            if (client != null)
            {
                Disconnect(client.Id);
            }
        }

        private void KickAllButton_Click(object sender, RoutedEventArgs e)
        {
            DisconnectAll();
        }

        private void AddToGrid(long id, ClientObject client)
        {
            Dispatcher.Invoke(() =>
            {
                var data = new ClientModel { Id = id, Username = client.Username };

                clientsDG.Items.Add(data);
            });
        }

        private void RemoveFromGrid(long id)
        {
            Dispatcher.Invoke(() =>
            {
                foreach (ClientModel c in clientsDG.Items)
                {
                    if (c.Id == id)
                    {
                        clientsDG.Items.Remove(c);
                        break;
                    }
                }
            });
        }
    }
}
