﻿namespace KBEngine
{
  	using UnityEngine; 
	using System; 
	using System.Collections; 
	using System.Collections.Generic;
	
	/*
		KBEngine逻辑层的实体基础类
		所有扩展出的游戏实体都应该继承于该模块
	*/
    public class Entity 
    {
		// 当前玩家最后一次同步到服务端的位置与朝向
		// 这两个属性是给引擎KBEngine.cs用的，别的地方不要修改
		public Vector3 _entityLastLocalPos = new Vector3(0f, 0f, 0f);
		public Vector3 _entityLastLocalDir = new Vector3(0f, 0f, 0f);
		
    	public Int32 id = 0;
		public string className = "";
		public Vector3 position = new Vector3(0.0f, 0.0f, 0.0f);
		public Vector3 direction = new Vector3(0.0f, 0.0f, 0.0f);
		public float velocity = 0.0f;
		
		public bool isOnGround = true;
		
		public object renderObj = null;
		
		public Mailbox baseMailbox = null;
		public Mailbox cellMailbox = null;

        // 本地坐标
        public Vector3 localPosition = Vector3.zero;
        public Vector3 localDirection = Vector3.zero;

        // 父对象
        public Int32 parentID = 0;
        public Entity parent = null;

        // 子对象列表
        public Dictionary<Int32, Entity> children = new Dictionary<int, Entity>();

        // enterworld之后设置为true
        public bool inWorld = false;

		/// <summary>
		/// This property is True if it is a client-only entity.
		/// </summary>
		public bool isClientOnly = false;

		/// <summary>
		/// 对于玩家自身来说，它表示是否自己被其它玩家控制了；
		/// 对于其它entity来说，表示我本机是否控制了这个entity
		/// </summary>
		public bool isControlled = false;
		
		// __init__调用之后设置为true
		public bool inited = false;
        
		// entityDef属性，服务端同步过来后存储在这里
		private Dictionary<string, Property> defpropertys_ = 
			new Dictionary<string, Property>();

		private Dictionary<UInt16, Property> iddefpropertys_ = 
			new Dictionary<UInt16, Property>();

		public static void clear()
		{
		}

		public Entity()
		{
			foreach(Property e in EntityDef.moduledefs[GetType().Name].propertys.Values)
			{
				Property newp = new Property();
				newp.name = e.name;
				newp.utype = e.utype;
				newp.properUtype = e.properUtype;
				newp.properFlags = e.properFlags;
				newp.aliasID = e.aliasID;
				newp.defaultValStr = e.defaultValStr;
				newp.setmethod = e.setmethod;
				newp.val = newp.utype.parseDefaultValStr(newp.defaultValStr);
				defpropertys_.Add(e.name, newp);
				iddefpropertys_.Add(e.properUtype, newp);
			}
		}

        public virtual void destroy()
        {
            onDestroy();

            // 销毁自身只代表自己不见了，不代表对方没有父了
            // 因此这里只改变父对象的指向，但parentID的值仍然保留
            foreach (KeyValuePair<Int32, Entity> dic in children)
                dic.Value.parent = null;
            children.Clear();

            // 解引用
            if (parent != null)
            {
                parent.removeChild(this);
                parent = null;
            }

            renderObj = null;
        }

        public virtual void onDestroy ()
		{
		}
		
		public bool isPlayer()
		{
			return id == KBEngineApp.app.entity_id;
		}
		
		public void addDefinedProperty(string name, object v)
		{
			Property newp = new Property();
			newp.name = name;
			newp.properUtype = 0;
			newp.val = v;
			newp.setmethod = null;
			defpropertys_.Add(name, newp);
		}

		public object getDefinedProperty(string name)
		{
			Property obj = null;
			if(!defpropertys_.TryGetValue(name, out obj))
			{
				return null;
			}
		
			return defpropertys_[name].val;
		}
		
		public void setDefinedProperty(string name, object val)
		{
			defpropertys_[name].val = val;
		}
		
		public object getDefinedPropertyByUType(UInt16 utype)
		{
			Property obj = null;
			if(!iddefpropertys_.TryGetValue(utype, out obj))
			{
				return null;
			}
			
			return iddefpropertys_[utype].val;
		}
		
		public void setDefinedPropertyByUType(UInt16 utype, object val)
		{
			iddefpropertys_[utype].val = val;
		}
		
		/*
			KBEngine的实体构造函数，与服务器脚本对应。
			存在于这样的构造函数是因为KBE需要创建好实体并将属性等数据填充好才能告诉脚本层初始化
		*/
		public virtual void __init__()
		{
		}
		
		public virtual void callPropertysSetMethods()
		{
			foreach(Property prop in iddefpropertys_.Values)
			{
				object oldval = getDefinedPropertyByUType(prop.properUtype);
				System.Reflection.MethodInfo setmethod = prop.setmethod;
				
				if(setmethod != null)
				{
					if(prop.isBase())
					{
						if(inited && !inWorld)
						{
							//Dbg.DEBUG_MSG(className + "::callPropertysSetMethods(" + prop.name + ")"); 
							setmethod.Invoke(this, new object[]{oldval});
						}
					}
					else
					{
						if(inWorld)
						{
							if(prop.isOwnerOnly() && !isPlayer())
								continue;

							setmethod.Invoke(this, new object[]{oldval});
						}
					}
				}
				else
				{
					//Dbg.DEBUG_MSG(className + "::callPropertysSetMethods(" + prop.name + ") not found set_*"); 
				}
			}
		}
		
		public void baseCall(string methodname, params object[] arguments)
		{			
			if(KBEngineApp.app.currserver == "loginapp")
			{
				Dbg.ERROR_MSG(className + "::baseCall(" + methodname + "), currserver=!" + KBEngineApp.app.currserver);  
				return;
			}

			Method method = null;
			if(!EntityDef.moduledefs[className].base_methods.TryGetValue(methodname, out method))
			{
				Dbg.ERROR_MSG(className + "::baseCall(" + methodname + "), not found method!");  
				return;
			}
			
			UInt16 methodID = method.methodUtype;
			
			if(arguments.Length != method.args.Count)
			{
				Dbg.ERROR_MSG(className + "::baseCall(" + methodname + "): args(" + (arguments.Length) + "!= " + method.args.Count + ") size is error!");  
				return;
			}
			
			baseMailbox.newMail();
			baseMailbox.bundle.writeUint16(methodID);
			
			try
			{
				for(var i=0; i<method.args.Count; i++)
				{
					if(method.args[i].isSameType(arguments[i]))
					{
						method.args[i].addToStream(baseMailbox.bundle, arguments[i]);
					}
					else
					{
						throw new Exception("arg" + i + ": " + method.args[i].ToString());
					}
				}
			}
			catch(Exception e)
			{
				Dbg.ERROR_MSG(className + "::baseCall(method=" + methodname + "): args is error(" + e.Message + ")!");  
				baseMailbox.bundle = null;
				return;
			}
			
			baseMailbox.postMail(null);
		}
		
		public void cellCall(string methodname, params object[] arguments)
		{
			if(KBEngineApp.app.currserver == "loginapp")
			{
				Dbg.ERROR_MSG(className + "::cellCall(" + methodname + "), currserver=!" + KBEngineApp.app.currserver);  
				return;
			}
			
			Method method = null;
			if(!EntityDef.moduledefs[className].cell_methods.TryGetValue(methodname, out method))
			{
				Dbg.ERROR_MSG(className + "::cellCall(" + methodname + "), not found method!");  
				return;
			}
			
			UInt16 methodID = method.methodUtype;
			
			if(arguments.Length != method.args.Count)
			{
				Dbg.ERROR_MSG(className + "::cellCall(" + methodname + "): args(" + (arguments.Length) + "!= " + method.args.Count + ") size is error!");  
				return;
			}
			
			if(cellMailbox == null)
			{
				Dbg.ERROR_MSG(className + "::cellCall(" + methodname + "): no cell!");  
				return;
			}
			
			cellMailbox.newMail();
			cellMailbox.bundle.writeUint16(methodID);
				
			try
			{
				for(var i=0; i<method.args.Count; i++)
				{
					if(method.args[i].isSameType(arguments[i]))
					{
						method.args[i].addToStream(cellMailbox.bundle, arguments[i]);
					}
					else
					{
						throw new Exception("arg" + i + ": " + method.args[i].ToString());
					}
				}
			}
			catch(Exception e)
			{
				Dbg.ERROR_MSG(className + "::cellCall(" + methodname + "): args is error(" + e.Message + ")!");  
				cellMailbox.bundle = null;
				return;
			}

			cellMailbox.postMail(null);
		}
	
		public void enterWorld()
		{
			// Dbg.DEBUG_MSG(className + "::enterWorld(" + getDefinedProperty("uid") + "): " + id); 
			inWorld = true;
			
			try{
				onEnterWorld();
			}
			catch (Exception e)
			{
				Dbg.ERROR_MSG(className + "::onEnterWorld: error=" + e.ToString());
			}

			Event.fireOut("onEnterWorld", new object[]{this});
		}
		
		public virtual void onEnterWorld()
		{
		}

		public void leaveWorld()
		{
			// Dbg.DEBUG_MSG(className + "::leaveWorld: " + id); 
			inWorld = false;
			
			try{
				onLeaveWorld();
			}
			catch (Exception e)
			{
				Dbg.ERROR_MSG(className + "::onLeaveWorld: error=" + e.ToString());
			}

			Event.fireOut("onLeaveWorld", new object[]{this});
		}
		
		public virtual void onLeaveWorld()
		{
		}

		public virtual void enterSpace()
		{
			// Dbg.DEBUG_MSG(className + "::enterSpace(" + getDefinedProperty("uid") + "): " + id); 
			inWorld = true;
			
			try{
				onEnterSpace();
			}
			catch (Exception e)
			{
				Dbg.ERROR_MSG(className + "::onEnterSpace: error=" + e.ToString());
			}
			
			Event.fireOut("onEnterSpace", new object[]{this});
		}
		
		public virtual void onEnterSpace()
		{
		}
		
		public virtual void leaveSpace()
		{
			// Dbg.DEBUG_MSG(className + "::leaveSpace: " + id); 
			inWorld = false;
			
			try{
				onLeaveSpace();
			}
			catch (Exception e)
			{
				Dbg.ERROR_MSG(className + "::onLeaveSpace: error=" + e.ToString());
			}
			
			Event.fireOut("onLeaveSpace", new object[]{this});
		}

		public virtual void onLeaveSpace()
		{
		}
		
		public virtual void set_position(object old)
		{
			//Dbg.DEBUG_MSG(className + "::set_position: " + old + " => " + v); 
			
			if(isPlayer())
				KBEngineApp.app.entityServerPos(position);
			
			if(inWorld)
				Event.fireOut("set_position", new object[]{this});
		}

		/// <summary>
		/// 服务器更新易变数据
		/// </summary>
		public virtual void onUpdateVolatileData()
		{
		}

		/// <summary>
		/// 用于继承者重载，当Entity有父对象时，
		/// 其父对象改变了世界坐标或朝向时会在子对象身上会触发此方法。
		/// </summary>
		public virtual void onUpdateVolatileDataByParent()
		{
		}
		
		public virtual void set_direction(object old)
		{
			
			//Dbg.DEBUG_MSG(className + "::set_direction: " + old + " => " + v); 
			
			if(inWorld)
				Event.fireOut("set_direction", new object[]{this});
		}

		/// <summary>
		/// This callback method is called when the local entity control by the client has been enabled or disabled. 
		/// See the Entity.controlledBy() method in the CellApp server code for more infomation.
		/// </summary>
		/// <param name="isControlled">
		/// 对于玩家自身来说，它表示是否自己被其它玩家控制了；
		/// 对于其它entity来说，表示我本机是否控制了这个entity
		/// </param>
		public virtual void onControlled(bool isControlled_)
		{
		
		}

        /** 本地坐标与世界坐标互转 */
        public Vector3 positionLocalToWorld(Vector3 localPos)
        {
            Quaternion p_local = new Quaternion(localPos.x, localPos.y, localPos.z, 0);
      
			Quaternion qx_r = Quaternion.AngleAxis(direction.x, new Vector3(1, 0, 0));
			Quaternion qy_r = Quaternion.AngleAxis(direction.y, new Vector3(0, 1, 0));
			Quaternion qz_r = Quaternion.AngleAxis(direction.z, new Vector3(0, 0, 1));

            Quaternion q_r = qy_r * qx_r * qz_r; //欧拉旋转的旋转顺序是Z、X、Y，不同的旋转顺序方向，需要在这里修改，Z是最上层,qy*qx*qz，从右向左
            Quaternion q_rr = Quaternion.Inverse(q_r); //逆运算
            Quaternion p = q_r * p_local * q_rr; //p经过q_r四元数旋转得到p0，所以p=q*p0*q^-1

            return new Vector3(p.x + position.x, p.y + position.y, p.z + position.z);
        }

        public Vector3 positionWorldToLocal(Vector3 worldPos)
        {
			Quaternion qx_r = Quaternion.AngleAxis(direction.x, new Vector3(1, 0, 0));
			Quaternion qy_r = Quaternion.AngleAxis(direction.y, new Vector3(0, 1, 0));
			Quaternion qz_r = Quaternion.AngleAxis(direction.z, new Vector3(0, 0, 1));

            Quaternion q_r = qy_r * qx_r * qz_r; //欧拉旋转的旋转顺序是Z、X、Y，不同的旋转顺序方向，需要在这里修改，Z是最上层,qy*qx*qz，从右向左
            Quaternion q_rr = Quaternion.Inverse(q_r); //逆运算

            Vector3 g_pos = new Vector3(worldPos.x - position.x, worldPos.y - position.y, worldPos.z - position.z);
            Quaternion g_q = new Quaternion(g_pos.x, g_pos.y, g_pos.z, 0);
            Quaternion p_local = q_rr * g_q * q_r;

            return new Vector3(p_local.x, p_local.y, p_local.z);
        }

        public Vector3 directionLocalToWorld(Vector3 localDir)
        {

			Quaternion q_parentdir = Quaternion.Euler(direction);
			Quaternion q_childdir = Quaternion.Euler(localDir);

            Quaternion wr = q_parentdir * q_childdir;
            Vector3 result = wr.eulerAngles;

            return result;
        }

        public Vector3 directionWorldToLocal(Vector3 worldDir)
        {
			Quaternion q_parentdir = Quaternion.Euler(direction);
			Quaternion q_childworlddir = Quaternion.Euler(worldDir);

            Quaternion pr_r = Quaternion.Inverse(q_parentdir); //逆运算
            Quaternion lr = pr_r * q_childworlddir;

            Vector3 result = lr.eulerAngles;

			return result;
        }

        public void setParent(Entity ent)
        {
            if (ent == parent)
                return;

            Entity old = parent;
            if (parent != null)
            {
                parentID = 0;
                parent.removeChild(this);
                localPosition = position;
                localDirection = direction;
				parent = null;
				if (inWorld)
					onLoseParentEntity();
			}

            parent = ent;

            if (parent != null)
            {
                parentID = ent.id;
                parent.addChild(this);
                localPosition = parent.positionWorldToLocal(position);
                localDirection = parent.directionWorldToLocal(direction);
				if (inWorld)
					onGotParentEntity();
            }
        }

        /// <summary>
        /// 当获得父对象时，此方法被触发
        /// </summary>
        public virtual void onGotParentEntity()
        {
        }

        /// <summary>
        /// 当失去父对象时，此方法被触发
        /// </summary>
        public virtual void onLoseParentEntity()
        {
        }

        public void addChild(Entity ent)
        {
            children.Add(ent.id, ent);
        }

        public void removeChild(Entity ent)
        {
            children.Remove(ent.id);
        }

        public Entity getChild(Int32 eid)
        {
            Entity entity = null;
            children.TryGetValue(eid, out entity);
            return entity;
        }

        public void parentPositionChangedNotify()
        {
            if (children.Count == 0)
                return;

			foreach (KeyValuePair<Int32, Entity> dic in children)
			{
				Entity ent = dic.Value;

				// 更新世界坐标
				ent.position = positionLocalToWorld(ent.localPosition);
				ent.setDefinedProperty("position", ent.position);

				// 设置最后更新值，以避免被控制者向服务器发送世界坐标或朝向
				ent._entityLastLocalPos = ent.position;

				// 对于玩家自已或被本机控制的entity而言，因父对象的移动而移动，
				// 新坐标不需要通知服务器，因为每个客户端都会做同样的处理，服务器也会自行计算。
				if (ent.isPlayer() || ent.isControlled)
					ent._entityLastLocalPos = ent.position;

				ent.onUpdateVolatileDataByParent();
			}
        }

        public void parentDirectionChangedNotify()
        {
            if (children.Count == 0)
                return;

			foreach (KeyValuePair<Int32, Entity> dic in children)
			{
				Entity ent = dic.Value;

				// 父对象的朝向改变会引发子对象的世界坐标的改变
				ent.position = positionLocalToWorld(ent.localPosition);
				//ent.setDefinedProperty("position", ent.position);

				// 设置最后更新值，以避免被控制者向服务器发送世界坐标或朝向
				ent._entityLastLocalPos = ent.position;

				// 更新世界朝向
				ent.direction = directionLocalToWorld(ent.localDirection);
				//ent.setDefinedProperty("direction", ent.direction);

				// 设置最后更新值，以避免被控制者向服务器发送世界坐标或朝向
				ent._entityLastLocalDir = ent.direction;

				// 对于玩家自已或被本机控制的entity而言，因父对象的移动而移动，
				// 新坐标不需要通知服务器，因为每个客户端都会做同样的处理，服务器也会自行计算。
				if (ent.isPlayer() || ent.isControlled)
				{
					ent._entityLastLocalPos = ent.position;
					ent._entityLastLocalDir = ent.direction;
				}

				ent.onUpdateVolatileDataByParent();
			}
        }

		/// <summary>
		/// 内部接口，用于引擎设置entity的坐标
		/// </summary>
		/// <param name="pos"></param>
		internal void setPositionFromServer(Vector3 pos)
		{
			position = pos;
			//setDefinedProperty("position", pos);

			if (inWorld)
				parentPositionChangedNotify();
		}

		/// <summary>
		/// 内部接口，用于引擎设置entity的坐标
		/// </summary>
		/// <param name="dir"></param>
		internal void setDirectionFromServer(Vector3 dir)
		{
			direction = dir;
			//setDefinedProperty("direction", dir);

			if (inWorld)
				parentDirectionChangedNotify();
		}

		/// <summary>
		/// 用于被控制者（如角色）定期向服务器更新其世界坐标和世界朝向
		/// </summary>
		/// <param name="pos"></param>
		public void updateVolatileDataForServer(Vector3 pos, Vector3 dir)
		{
			if (Vector3.Distance(position, pos) > 0.001f)
			{

				position = pos;

				if (parent != null)
					localPosition = parent.positionWorldToLocal(pos);
				else
					localPosition = position;
			}

			if (Vector3.Distance(direction, dir) > 0.001f)
			{

				direction = dir;

				if (parent != null)
					localDirection = parent.directionWorldToLocal(direction);
				else
					localDirection = dir;
			}

			onUpdateVolatileDataByParent();
		}



    }

}
