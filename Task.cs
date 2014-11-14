using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace KBEngine
{
	/// <summary>
	/// phw(acatadog):
	/// 在同一个线程上的异步任务，类似于MonoBehaviour下的StopCoroutine()概念，
	/// 这个的存在是为了使Entity在还没有unity3d GameObject/MonoBehaviour的时候能做一些异步的任务。
	/// </summary>
	public abstract class Task
	{
		static Task s_begin = null;
		static Task s_last = null;
		static Int32 s_lastID = 0;
		static Dictionary<Int32, Task> s_tasks = new Dictionary<int, Task>();

		static Int32 NewID()
		{
			return ++s_lastID;
		}
		
		static void add( Task task )
		{
			if (s_begin == null)
			{
				s_begin = task;
				s_last = task;
				task._next = null;
				task._prev = null;
			}
			else
			{
				task._prev = s_last;
				s_last._next = task;
				task._next = null;
				s_last = task;
			}

			s_tasks.Add( task._id, task );
		}
		
		static void remove( Task task )
		{
			Task prev = task._prev;
			Task next = task._next;
			task._prev = null;
			task._next = null;
			
			if (prev == null)
			{
				if (next == null)
				{
					s_begin = null;
					s_last = null;
				}
				else
				{
					next._prev = null;
					s_begin = next;
				}
			}
			else
			{
				if (next == null)
				{
					prev._next = null;
					s_last = prev;
				}
				else
				{
					prev._next = next;
					next._prev = prev;
				}
			}

			s_tasks.Remove( task._id );
		}
		
		public static void remove( Int32 taskID )
		{
			if (s_tasks.ContainsKey( taskID ))
				remove( s_tasks[taskID] );
		}
		
		public static void updateAll()
		{
			if (s_tasks.Count == 0)
				return;

			Task next = s_begin;
			while (next != null)
			{
				// 先移动到下一个任务，以应对在处理任务时存在删除任务的情况
				Task old = next;
				next = next._next;

				old.onUpdate();
			}
		}

		Task _prev;
		Task _next;
		Int32 _id;

		public Task()
		{

		}

		~Task()
		{
			Dbg.DEBUG_MSG( string.Format( "Task::~Task(), task id = {0}", _id ) );
		}

		public Int32 id
		{
			get { return _id; }
		}

		/// <summary>
		/// 开始任务
		/// </summary>
		public Int32 start ()
		{
			_id = NewID();
			add( this );
			onStart();
			return _id;
		}

		/// <summary>
		/// 结束任务
		/// </summary>
		public void stop()
		{
			remove( this );
			onStop();
		}
		
		/// <summary>
		/// Update is called once per frame
		/// 每一帧做一些事情
		/// </summary>
		protected virtual void onUpdate ()
		{
		
		}

		/// <summary>
		/// 开始任务时做一些事情
		/// </summary>
		protected virtual void onStart ()
		{
			
		}
		
		/// <summary>
		/// 结束任务时做一些事情
		/// </summary>
		protected virtual void onStop()
		{
			
		}
	}

}
