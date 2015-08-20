namespace KBEngine
{
  	using UnityEngine; 
	using System; 
	using System.Net.Sockets; 
	using System.Net; 
	using System.Collections; 
	using System.Collections.Generic;
	using System.Text;
	using System.Text.RegularExpressions;
	using System.Threading;
	
	/*
		网络模块
		处理连接、收发数据
	*/
	public class NetworkInterface3 : NetworkInterface
    {

		/*
			包接收模块(与服务端网络部分的名称对应)
			处理网络数据的接收
		*/
		public class PacketReceiver
		{
			private MessageReader messageReader = null;

			private byte[] _buffer;

			// socket向缓冲区写的起始位置
			int _wpos = 0;

			// 主线程读取数据的起始位置
			int _rpos = 0;

			public PacketReceiver()
			{
				_buffer = new byte[KBEngineApp.app.getInitArgs().RECV_BUFFER_MAX];

				messageReader = new MessageReader();
			}

			public void processMessage()
			{
				int t_wpos = Interlocked.Add(ref _wpos, 0);

				if (_rpos < t_wpos)
				{
					messageReader.process(_buffer, (UInt32)_rpos, (UInt32)(t_wpos - _rpos));
					Interlocked.Exchange(ref _rpos, t_wpos);
				}
				else if (t_wpos < _rpos)
				{
					messageReader.process(_buffer, (UInt32)_rpos, (UInt32)(_buffer.Length - _rpos));
					messageReader.process(_buffer, (UInt32)0, (UInt32)t_wpos);
					Interlocked.Exchange(ref _rpos, t_wpos);
				}
				else
				{
					// 没有可读数据
				}
			}

			int _free()
			{
				int t_rpos = Interlocked.Add(ref _rpos, 0);

				if (_wpos == _buffer.Length)
				{
					if (t_rpos == 0)
					{
						return 0;
					}

					Interlocked.Exchange(ref _wpos, 0);
				}

				if (t_rpos <= _wpos)
				{
					return _buffer.Length - _wpos;
				}

				return t_rpos - _wpos - 1;
			}

			public void process(NetworkInterface3 networkInterface)
			{
				var socket = networkInterface.sock();

				try
				{
					if (!socket.Poll(0, SelectMode.SelectRead))
						return;
				}
				catch (Exception e)
				{
					Dbg.ERROR_MSG(string.Format("PacketReceiver::process(): socket error! {0}", e.ToString()));
					return;
				}

				// 必须有空间可写，否则我们阻塞在线程中直到有空间为止
				int first = 0;
				int space = _free();

				//Dbg.DEBUG_MSG(string.Format("PacketReceiver::process(), will enter receive data status, buff free = '{0}'", space));

				while (space == 0)
				{
					if (first > 0)
					{
						if (first > 1000)
							throw new Exception("PacketReceiver::process(): no space!");

						Dbg.WARNING_MSG("PacketReceiver::process(): waiting for space, Please adjust 'RECV_BUFFER_MAX'! retries=" + first);
						System.Threading.Thread.Sleep(5);
					}

					first += 1;
					space = _free();
				}

				int bytesRead = 0;
				try
				{
					// Read data from the remote device.
					bytesRead = socket.Receive(_buffer, _wpos, space, 0);
				}
				catch (Exception e)
				{
					Dbg.ERROR_MSG("PacketReceiver::process(): call Receive() is err: " + e.ToString());
					Event.fireIn("_closeNetwork", new object[] { networkInterface });
					return;
				}

				if (bytesRead > 0)
				{
					// 更新写位置
					Interlocked.Add(ref _wpos, bytesRead);
				}
				else
				{
					Dbg.WARNING_MSG(string.Format("PacketReceiver::process(): disconnect! bytesRead = '{0}'", bytesRead));
					Event.fireIn("_closeNetwork", new object[] { networkInterface });
					return;
				}
			}
		}



		/*
			包发送模块(与服务端网络部分的名称对应)
			处理网络数据的发送
		*/
		public class PacketSender
		{
			private byte[] _buffer;

			int _wpos = 0;				// 写入的数据位置
			int _spos = 0;				// 发送完毕的数据位置
			int _sending = 0;

			public PacketSender()
			{
				_buffer = new byte[KBEngineApp.app.getInitArgs().SEND_BUFFER_MAX];

				_wpos = 0;
				_spos = 0;
				_sending = 0;
			}

			public bool send(byte[] datas)
			{
				if (datas.Length <= 0)
					return true;

				if (Interlocked.CompareExchange(ref _sending, 1, 0) == 0)
				{
					if (_wpos == _spos)
					{
						_wpos = 0;
						_spos = 0;
					}
				}

				int t_spos = Interlocked.Add(ref _spos, 0);
				int space = 0;
				int tt_wpos = _wpos % _buffer.Length;
				int tt_spos = t_spos % _buffer.Length;

				if (tt_wpos >= tt_spos)
					space = _buffer.Length - tt_wpos + tt_spos - 1;
				else
					space = tt_spos - tt_wpos - 1;

				if (datas.Length > space)
				{
					Dbg.ERROR_MSG("PacketSender::send(): no space, Please adjust 'SEND_BUFFER_MAX'! data(" + datas.Length
						+ ") > space(" + space + "), wpos=" + _wpos + ", spos=" + t_spos);

					return false;
				}

				int expect_total = tt_wpos + datas.Length;
				if (expect_total <= _buffer.Length)
				{
					Array.Copy(datas, 0, _buffer, tt_wpos, datas.Length);
				}
				else
				{
					int remain = _buffer.Length - tt_wpos;
					Array.Copy(datas, 0, _buffer, tt_wpos, remain);
					Array.Copy(datas, remain, _buffer, 0, expect_total - _buffer.Length);
				}

				Interlocked.Add(ref _wpos, datas.Length);

				return true;
			}

			public void process(NetworkInterface3 networkInterface)
			{
				var socket = networkInterface.sock();

				try
				{
					if (!socket.Poll(0, SelectMode.SelectWrite))
						return;
				}
				catch (Exception e)
				{
					Dbg.ERROR_MSG(string.Format("PacketSender::process(): socket error! {0}", e.ToString()));
					return;
				}

				int sendSize = Interlocked.Add(ref _wpos, 0) - _spos;
				if (sendSize <= 0)
					return;
				int t_spos = _spos % _buffer.Length;
				if (t_spos == 0)
					t_spos = sendSize;

				if (sendSize > _buffer.Length - t_spos)
					sendSize = _buffer.Length - t_spos;

				int bytesSent = 0;
				try
				{
					bytesSent = socket.Send(_buffer, _spos % _buffer.Length, sendSize, 0);
				}
				catch (Exception e)
				{
					Dbg.ERROR_MSG("PacketSender::process(): is err: " + e.ToString());
					Event.fireIn("_closeNetwork", new object[] { networkInterface });
				}

				int spos = Interlocked.Add(ref _spos, bytesSent);

				//Dbg.DEBUG_MSG(string.Format("PacketSender:process(), send '{0}' bytes, spos = '{1}'", bytesSent, spos));

				if (spos == Interlocked.Add(ref _wpos, 0))
				{
					// 所有数据发送完毕了
					Interlocked.Exchange(ref _sending, 0);
				}
			}
		}

	
		
		public interface Status_Base
		{
			void process();
		}
		
		public class Status_Connecting : Status_Base
		{
			public NetworkInterface3 networkInterface;

			int step;
			ConnectState _state = new ConnectState();

			public Status_Connecting(NetworkInterface3 networkInterface_)
			{
				this.networkInterface = networkInterface_;
			}

			public void connectTo(string ip, int port, ConnectCallback callback, object userData)
			{
				_state.connectIP = ip;
				_state.connectPort = port;
				_state.connectCB = callback;
				_state.userData = userData;
				_state.socket = networkInterface.makeDefaultSocket();
				_state.networkInterface = networkInterface;
				this.step = 0;

				Dbg.DEBUG_MSG("connect to " + ip + ":" + port + " ...");

				try
				{
					_state.socket.Connect(ip, port);
				}
				catch (SocketException se)
				{
					if (se.SocketErrorCode == SocketError.WouldBlock)
					{
						step = 1;
					}
					else
					{
						Dbg.ERROR_MSG(string.Format("connect to '{0}:{1}' fault!!! error = '{2}'", ip, port, se));
						_state.error = se.ToString();
						Event.fireIn("_onConnectStatus", new object[] { _state });
					}
				}
				catch (Exception e)
				{
					Dbg.ERROR_MSG(string.Format("connect to '{0}:{1}' fault!!! error = '{2}'", ip, port, e));
					_state.error = e.ToString();
					Event.fireIn("_onConnectStatus", new object[] { _state });
				}
				step = 1;
			}

			public virtual void process()
			{
				//Dbg.WARNING_MSG("Status_Connecting::process(), step = " + step);
				if (step == 1)
				{
					bool result = false;

					try
					{
						// 每0.1秒检查一次
						result = networkInterface._socket.Poll((int)(1000 * 1000 * 0.1), SelectMode.SelectWrite);
					}
					catch (Exception e)
					{
						networkInterface._network_status = null;
						_state.error = e.ToString();
						Event.fireIn("_onConnectStatus", new object[] { _state });
						return;
					}

					if (result)
					{
						step = 2;
						// 切換到已连接状态
						networkInterface._network_status = networkInterface._status_connected;

						// 回调通知
						Event.fireIn("_onConnectStatus", new object[] { _state });
					}
				}
			}
		}

		public class Status_Connected : Status_Base
		{
			public NetworkInterface3 networkInterface;


			public Status_Connected(NetworkInterface3 networkInterface_)
			{
				this.networkInterface = networkInterface_;
			}

			public virtual void process()
			{
				if (networkInterface._packetReceiver != null)
					networkInterface._packetReceiver.process(networkInterface);

				if (networkInterface._packetSender != null)
					networkInterface._packetSender.process(networkInterface);

				Thread.Sleep((int)(1000 * 0.1));
			}

		}



		PacketReceiver _packetReceiver = null;
		PacketSender _packetSender = null;

		Thread _worker = null;
		Status_Base _network_status = null;
		Status_Connecting _status_connecting = null;
		Status_Connected _status_connected = null;



        public NetworkInterface3()
        {
			reset();
        }

		~NetworkInterface3()
		{
			Dbg.DEBUG_MSG("NetworkInterface::~NetworkInterface(), destructed!!!");
			reset();
		}

		public Socket makeDefaultSocket()
		{
			if (_socket != null)
				return _socket;

			_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			_socket.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, KBEngineApp.app.getInitArgs().getRecvBufferSize() * 2);
			_socket.NoDelay = true;
			_socket.Blocking = false;
			SetKeepAlive(_socket, 5000, 10000);
			return _socket;
		}

		public override void reset()
		{
			if (_worker != null)
			{
				_worker.Interrupt();
				_worker = null;
			}
			_packetReceiver = null;
			_packetSender = null;
			_network_status = null;
			_status_connecting = new Status_Connecting(this);
			_status_connected = new Status_Connected(this);
			base.reset();
		}

		public override void close()
		{
			if (_worker != null)
			{
				_worker.Interrupt();
				_worker = null;
			}
			_packetReceiver = null;
			_packetSender = null;
			_network_status = null;
			base.close();
		}

		public override void _onConnectStatus(ConnectState state)
		{
			KBEngine.Event.deregisterIn(this);
			
			bool success = (state.error == "" && valid());
			if(success)
			{
				Dbg.DEBUG_MSG(string.Format("NetworkInterface::_onConnectStatus(), connect to {0} is success!", state.socket.RemoteEndPoint.ToString()));
				_packetReceiver = new PacketReceiver();
				_packetSender = new PacketSender();
			}
			else
			{
				Dbg.ERROR_MSG(string.Format("NetworkInterface::_onConnectStatus(), connect is error! ip: {0}:{1}, err: {2}", state.connectIP, state.connectPort, state.error));
			}
			
			Event.fireAll("onConnectStatus", new object[]{success});
			
			if (state.connectCB != null)
				state.connectCB(state.connectIP, state.connectPort, success, state.userData);
		}
		
		public void loop()
		{
			try
			{
				while (_network_status != null)
				{
					_network_status.process();
				}
			}
			catch (ThreadInterruptedException)
			{
				Dbg.DEBUG_MSG("NetworkInterface::loop(), receive interrupted signal, stop thread now!");
			}
		}

		public override void connectTo(string ip, int port, ConnectCallback callback, object userData) 
		{
			if (valid())
				throw new InvalidOperationException( "Have already connected!" );
			
			if(!(new Regex( @"((?:(?:25[0-5]|2[0-4]\d|((1\d{2})|([1-9]?\d)))\.){3}(?:25[0-5]|2[0-4]\d|((1\d{2})|([1-9]?\d))))")).IsMatch(ip))
			{
				IPHostEntry ipHost = Dns.GetHostEntry (ip);
				ip = ipHost.AddressList[0].ToString();
			}

			// 先注册一个事件回调，该事件在当前线程触发
			Event.registerIn("_onConnectStatus", this, "_onConnectStatus");

			_network_status = _status_connecting;
			_status_connecting.connectTo(ip, port, callback, userData);
			_worker = new Thread(new ThreadStart(this.loop));
			_worker.Name = "NetWorkInterfaceThread";
			_worker.Start();
		}

		public override bool send(byte[] datas)
        {
			if(!valid()) 
			{
			   throw new ArgumentException ("invalid socket!");
			}
			
			return _packetSender.send(datas);
        }

		public override void process()
        {
        	if(!valid())
        		return;

			if (_packetReceiver != null)
				_packetReceiver.processMessage();
        }


		public static void SetKeepAlive(Socket socket, ulong keepalive_time, ulong keepalive_interval)
		{
			int bytes_per_long = 32 / 8;
			byte[] keep_alive = new byte[3 * bytes_per_long];
			ulong[] input_params = new ulong[3];
			int i1;
			int bits_per_byte = 8;

			if (keepalive_time == 0 || keepalive_interval == 0)
				input_params[0] = 0;
			else
				input_params[0] = 1;
			input_params[1] = keepalive_time;
			input_params[2] = keepalive_interval;
			for (i1 = 0; i1 < input_params.Length; i1++)
			{
				keep_alive[i1 * bytes_per_long + 3] = (byte)(input_params[i1] >> ((bytes_per_long - 1) * bits_per_byte) & 0xff);
				keep_alive[i1 * bytes_per_long + 2] = (byte)(input_params[i1] >> ((bytes_per_long - 2) * bits_per_byte) & 0xff);
				keep_alive[i1 * bytes_per_long + 1] = (byte)(input_params[i1] >> ((bytes_per_long - 3) * bits_per_byte) & 0xff);
				keep_alive[i1 * bytes_per_long + 0] = (byte)(input_params[i1] >> ((bytes_per_long - 4) * bits_per_byte) & 0xff);
			}

			socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, keep_alive);
		}
	}
} 
