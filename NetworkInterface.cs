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
	
	using MessageID = System.UInt16;
	using MessageLength = System.UInt16;
	
    public class NetworkInterface 
    {
    	public const int TCP_PACKET_MAX = 1460;
		public delegate void ConnectCallback( string ip, int port, bool success, object userData );

		private Socket socket_ = null;

		// for recv data from server
		MessageReceiver _messageReceiver;

		// for send data to server
		MessageSender _messageSender;

		// for connect
		string _connectIP;
		int _connectPort;
		ConnectCallback _connectCB;
		object _userData;

        public NetworkInterface(KBEngineApp app)
        {
			_messageReceiver = new MessageReceiver( this );
			_messageSender = new MessageSender( this );
        }

		public Socket sock()
		{
			return socket_;
		}
		
		public bool valid()
		{
			return ((socket_ != null) && (socket_.Connected == true));
		}
		
		void connectCB(object sender, SocketAsyncEventArgs e)
		{
			Dbg.INFO_MSG(string.Format("NetworkInterface::connectCB(), connect callback. ip: {0}:{1}, {2}", _connectIP, _connectPort, e.SocketError));
			switch (e.SocketError)
			{
			case SocketError.Success:
				if (_connectCB != null)
					_connectCB( _connectIP, _connectPort, true, _userData );
				Event.fireAll("onConnectStatus", new object[]{true});
				break;

			default:
				if (_connectCB != null)
					_connectCB( _connectIP, _connectPort, false, _userData );
				Event.fireAll("onConnectStatus", new object[]{false});
				break;
			}
		}
	    
		public void connect(string ip, int port, ConnectCallback cb, object userData ) 
		{
			if (valid())
				throw new InvalidOperationException( "Don't re-connect while connected." );

			_connectIP = ip;
			_connectPort = port;
			_connectCB = cb;
			_userData = userData;
			_messageReceiver = new MessageReceiver( this );
			_messageSender = new MessageSender( this );

			
			Regex rx = new Regex( @"((?:(?:25[0-5]|2[0-4]\d|((1\d{2})|([1-9]?\d)))\.){3}(?:25[0-5]|2[0-4]\d|((1\d{2})|([1-9]?\d))))");
			if (rx.IsMatch(ip))
			{
			}else
			{
				IPHostEntry ipHost = Dns.GetHostEntry (ip);
				ip = ipHost.AddressList[0].ToString();
			}

			// Security.PrefetchSocketPolicy(ip, 843);
			socket_ = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp); 
			socket_.SetSocketOption (System.Net.Sockets.SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, MemoryStream.BUFFER_MAX);

			bool result = false;

	        IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(ip), port); 
        
			SocketAsyncEventArgs connectEventArgs = new SocketAsyncEventArgs();
			connectEventArgs.RemoteEndPoint = endpoint;
			connectEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>(connectCB);
			Dbg.INFO_MSG(string.Format("NetworkInterface::connect(), will connect to {0}:{1};", _connectIP, _connectPort));

			try 
			{ 
				result = socket_.ConnectAsync(connectEventArgs);
            } 
            catch (Exception e) 
            {
				Dbg.WARNING_MSG(string.Format("NetworkInterface::connect(), connected error! ip: {0}:{1}, {2}", _connectIP, _connectPort, e.ToString()));
                
				Event.fireAll("onConnectStatus", new object[]{false});
            } 

			if ( !result )
			{
				// completed imimmediate
				connectCB( this, connectEventArgs );
			}
		}
        
        public void close()
        {
           if(socket_ != null && socket_.Connected)
			{
				socket_.Close(0);
				socket_ = null;
				Event.fireAll("onDisableConnect", new object[]{});
               
            }

            socket_ = null;
			_connectIP = "";
			_connectPort = 0;
			_connectCB = null;
			_userData = null;
			
			_messageReceiver = null;
			_messageSender = null;
		}

		public bool send( byte[] datas )
		{
			if(socket_ == null || socket_.Connected == false) 
			{
				throw new ArgumentException ("invalid socket!");
			}
			
			try
			{
				return _messageSender.send( datas );
			}
			catch (SocketException err)
			{
				Dbg.DEBUG_MSG("NetworkInterface::send(): call SendAsync() fault! err: " + err );
				close();
			}
			return false;
		}

		public void process()
		{
			if (valid())
			{
					_messageReceiver.process();

				// @todo(phw): 
				// 上面的receive数据时，有可能触发某些行为导致自身被释放，所以要加此判断
				// 例如：收到来自loginapp的登录成功消息时，切断当前链接，执行登录baseapp的行为
				// 之所以出现这样的问题，主要还是外部调用方面没有做相关优化，导致逻辑混乱所致
				// 这个以后有机会再进行修改
				if (_messageSender != null)
					_messageSender.process();
			}
		}
	}

	class MessageReceiver
	{
		public const MessageLength RECV_BUFFER_MAX = 32767;

		NetworkInterface _networkInterface = null;
		byte[] _buffer;  // ring buffer
		MessageLength _rptr = 0;  // 读指向
		MessageLength _wptr = 0;  // 写指向

		SocketAsyncEventArgs _socketEventArgs = new SocketAsyncEventArgs();
		MessageReader msgReader = new MessageReader();
		bool _recving = false;

		public MessageReceiver( NetworkInterface network )
		{
			init( network, RECV_BUFFER_MAX );
		}

		public MessageReceiver( NetworkInterface network, MessageLength buffLen )
		{
			init( network, buffLen );
		}

		void init( NetworkInterface network, MessageLength buffLen )
		{
			_networkInterface = network;
			_buffer = new byte[buffLen];
			msgReader = new MessageReader();
			_rptr = 0;  // 读指向
			_wptr = 0;  // 写指向
			_recving = false;

			// init _socketEventArgs param
			_socketEventArgs = new SocketAsyncEventArgs();
			_socketEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>( _recvCB );
		}

		/// <summary>
		/// 处理数据读请求，由主线程调用
		/// </summary>
		public void process()
		{
			// 1.处理已读取的数据
			if (_rptr > _wptr)
			{
				msgReader.process(_buffer, _rptr, (MessageLength)(_buffer.Length - _rptr));
				_rptr = 0;
			}

			if (_rptr < _wptr)
			{
				msgReader.process(_buffer, _rptr, (MessageLength)(_wptr - _rptr));
				_rptr = _wptr;
			}

			// 2.尝试请求读取数据
			recv();
		}

		void recv()
		{
			// 同一时间仅允许一个接收处理器即可，否则在没有可接收的数据时，线程将有可能无限多
			// 同时也是为了避免两个以上线程同时读写_wptr指针
			// @todo(phw): 
			// 以现在的机制，在接收数据的过程中是有可能把自身的网络给stop掉的，因此需要对网络的有效性进行校验
			// 例如：收到来自loginapp的登录成功消息时，切断当前链接，执行登录baseapp的行为
			// 之所以出现这样的问题，主要还是外部调用方面没有做相关优化，导致逻辑混乱所致
			// 这个以后有机会再进行修改
			if (_recving || _networkInterface == null || !_networkInterface.valid())
				return;

			// 能进来这里就表明没有别的线程需要读写_wptr和_rptr，所以下面的操作是安全的
			int wcount = 0;
			if (_wptr < _rptr)
			{
				// 减1是为了避免在这种情况下写指针主动与读指针重叠，以避免造成越界的问题
				wcount = _rptr - _wptr - 1;
			}
			else
			{
				wcount = _buffer.Length - _wptr;
			}

			if (wcount <= 0)
			{
				Dbg.WARNING_MSG(string.Format("NetworkInterface::MessageReceiver::recv(), no space to read data. wptr = {0}, rptr = {1};", _wptr, _rptr));
				return;
			}

			_recving = true;
			//if (wcount > MemoryStream.BUFFER_MAX)
			//	wcount = MemoryStream.BUFFER_MAX;
			_socketEventArgs.SetBuffer( _buffer, _wptr, wcount );
			try
			{
				//Dbg.DEBUG_MSG(string.Format("NetworkInterface::MessageReceiver::recv(): will async receive: wptr = {0}, rptr = {1};", _wptr, _rptr));
				if (!_networkInterface.sock().ReceiveAsync( _socketEventArgs ))
				{
					_processRecved( _socketEventArgs );
				}
			}
			catch (SocketException err)
			{
				Dbg.DEBUG_MSG("NetworkInterface::MessageReceiver::recv(): call ReceiveAsync() fault! err: " + err );
				_networkInterface.close();
			}
		}
		
		void _recvCB(object sender, SocketAsyncEventArgs e)
		{
			_processRecved( e );
		}

		void _processRecved(SocketAsyncEventArgs e)
		{
			//Dbg.WARNING_MSG( "NetworkInterface::MessageReceiver::_processRecved(), ManagedThreadId: " + Thread.CurrentThread.ManagedThreadId );

			if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
			{
				MessageLength hopeWptr = (MessageLength)(_wptr + e.BytesTransferred);
				if ( (_wptr < _rptr && hopeWptr >= _rptr) ||
				    hopeWptr > _buffer.Length ||
				    (hopeWptr == _buffer.Length && _rptr == 0) )
				{
					// 理论上这个是不可能进来的，如果进来了就表明某处逻辑有bug
					Dbg.ERROR_MSG(string.Format("NetworkInterface::_processRecved(): buffer overflow. wptr = {0}, rptr = {1}, BytesTransferred = {2}", _wptr, _rptr, e.BytesTransferred));
					_networkInterface.close();
				}
				_wptr = hopeWptr;
			}
			else
			{
				Dbg.WARNING_MSG( string.Format("NetworkInterface::_processRecved(): error found! SocketError: {0}, BytesTransferred: {1}", e.SocketError, e.BytesTransferred) );
				_networkInterface.close();
			}

			// 放在最后恢复状态，以使保证前面的处理安全
			_recving = false;
		}
	}

	public class MessageSender
	{
		public const int SEND_BUFFER_MAX = 32767;

		NetworkInterface _networkInterface = null;
		byte[] _buffer;  // ring buffer
		int _rptr = 0;  // 读指向
		int _wptr = 0;  // 写指向
		bool _sending = false;  // 指示当前是否正在请求发送中

		SocketAsyncEventArgs _socketEventArgs;


		public MessageSender( NetworkInterface network )
		{
			init( network, SEND_BUFFER_MAX );
		}
		
		public MessageSender(  NetworkInterface network, int buffLen )
		{
			init( network, buffLen );
		}
		
		void init(NetworkInterface network, int buffLen)
		{
			_networkInterface = network;
			_buffer = new byte[buffLen];
			_rptr = 0;  // 读指向
			_wptr = 0;  // 写指向
			_sending = false;
		
			_socketEventArgs = new SocketAsyncEventArgs();
			_socketEventArgs.Completed += new EventHandler<SocketAsyncEventArgs>( _sendCB );
		}

		int calcSendAmount()
		{
			if (_rptr == _wptr)
				return 0;

			if (_rptr < _wptr)
				return _wptr - _rptr;
			else
				return _buffer.Length - _rptr;
		}

		/// <summary>
		/// Send the specified datas. 由主线程调用
		/// </summary>
		/// <returns><c>true</c>, 如果要发送的数据正确的压入了缓冲区, <c>false</c> 缓冲区已满，数据溢出.</returns>
		/// <param name="datas">Datas.</param>
		public bool send( byte[] datas )
		{
			if (datas.Length > _buffer.Length)  // 这个判断必须有，否则下面的判断会有漏洞
			{
				Dbg.ERROR_MSG("NetworkInterface::MessageSender::send(), data length > " + _buffer.Length);
				return false;
			}

			//Dbg.DEBUG_MSG(string.Format("NetworkInterface::MessageSender::send(): wptr = {0}, rptr = {1}, Length = {2}", _wptr, _rptr, datas.Length));

			int hopeWptr = _wptr + datas.Length;
			if ( ( _wptr < _rptr && hopeWptr >= _rptr ) ||  // 写指针小于读指针时，结果不能大于或等于当前的读指针
			    ( hopeWptr >= _buffer.Length && (hopeWptr % _buffer.Length) >= _rptr ) )  // 写指针轮转后不能大于或等于当前的读指针
			{
				Dbg.ERROR_MSG("NetworkInterface::MessageSender::send(), buff overflow > " + _buffer.Length);
				return false;
			}

			if (hopeWptr < _buffer.Length)
			{
				datas.CopyTo( _buffer, _wptr );
				_wptr = hopeWptr;
			}
			else
			{
				int c1 = _buffer.Length - _wptr;
				Array.Copy( datas, 0, _buffer, _wptr, c1 );
				Array.Copy( datas, c1, _buffer, 0, datas.Length - c1 );
				_wptr = hopeWptr % datas.Length;
			}

			return true;
		}

		/// <summary>
		/// 处理发送请求，由主线程调用
		/// </summary>
		public void process()
		{
			// 同一时间仅存在一个发送处理请求，否则将出现多个线程读写同一个变量的问题
			// @todo(phw): 
			// 以现在的机制，在接收数据的过程中是有可能把自身的网络给stop掉的，因此需要对网络的有效性进行校验
			// 例如：收到来自loginapp的登录成功消息时，切断当前链接，执行登录baseapp的行为
			// 之所以出现这样的问题，主要还是外部调用方面没有做相关优化，导致逻辑混乱所致
			// 这个以后有机会再进行修改
			if (_sending || _networkInterface == null || !_networkInterface.valid())
				return;

			int sendAmount = calcSendAmount();
			if (sendAmount > 0)
			{
				_sending = true;
				_socketEventArgs.SetBuffer( _buffer, _rptr, sendAmount );
				//Dbg.DEBUG_MSG(string.Format("NetworkInterface::MessageSender::process(): will send data to server. wptr = {0}, rptr = {1}, amount = {2}", _wptr, _rptr, sendAmount));
				if ( !_networkInterface.sock().SendAsync( _socketEventArgs ) )
				{
					_processSent( _socketEventArgs );
				}
			}
		}

		void _sendCB(object sender, SocketAsyncEventArgs e)
		{
			_processSent( e );
		}

		void _processSent( SocketAsyncEventArgs e )
		{
			//Dbg.WARNING_MSG( "NetworkInterface::MessageSender::_processSent(), ManagedThreadId: " + Thread.CurrentThread.ManagedThreadId );
			//Dbg.DEBUG_MSG(string.Format("NetworkInterface::MessageSender::_processSent(): send data to server over. wptr = {0}, rptr = {1}, BytesTransferred = {2}", _wptr, _rptr, e.BytesTransferred));
			if (e.BytesTransferred > 0 && e.SocketError == SocketError.Success)
			{
				_rptr += e.BytesTransferred;
				if (_rptr >= _buffer.Length)
				{
					_rptr = 0;
				}
			}
			else
			{
				Dbg.WARNING_MSG( string.Format("NetworkInterface::MessageSender::_processSent(): error found! SocketError: {0}, BytesTransferred: {1}", e.SocketError, e.BytesTransferred) );

				_networkInterface.close();
			}

			// 放在最后，以避免异步问题
			_sending = false;
		}
	}
} 
