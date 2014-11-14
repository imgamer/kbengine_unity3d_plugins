using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace KBEngine
{
	/// <summary>
	/// phw(acatadog):
	/// 一个简化的timer机制。这不是一个支持高精度的机制，设计之初也没有想要支持高精度的回调，如果想使用高精度的回调请回避^_^。
	/// 注：当前计时使用的是Time.time，如果这个在多线程下使用有问题的话再考虑換别的方案
	/// </summary>
	public class Timer : Task
	{
		public delegate void TimerCallback( Int32 timerID, object userData );

		float _start;
		float _interval;
		TimerCallback _callback;
		object _userdata;
		
		float _next;

		/// <summary>
		/// 注册一个触发器
		/// </summary>
		/// <param name="start">第一次触发的时间</param>
		/// <param name="interval">每次触发的时间，如果仅想触发一次则置值小于或等于0.0f</param>
		/// <param name="function">回调函数</param>
		/// <param name="userData">用户自定义回传数据，不需要可以置为null</param>
		public Timer( float start, float interval, TimerCallback function, object userdata )
		{
			_start = start;
			_interval = interval;
			_callback = function;
			_userdata = userdata;

			_next = Time.time + _start;
		}

		/// <summary>
		/// 依赖于外部每tick执行一次的更新接口
		/// </summary>
		protected override void onUpdate()
		{
			if (_next > Time.time)
				return;

			_callback( id, _userdata );
			if (_interval > 0.0f)
			{
				// 这里不使用 d.next = t，是为了在偶尔延时下尽量保证在一定的时间内执行的回调次数是固定的
				_next += _interval;
			}
			else
			{
				stop();
			}
		}
	}
}
