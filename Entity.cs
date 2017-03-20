namespace KBEngine
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
		
		public GameObject renderObj = null;
		
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

			ScriptModule module = null;
			if(!EntityDef.moduledefs.TryGetValue(className, out module))
			{
				Dbg.ERROR_MSG("entity::baseCall:  entity-module(" + className + ") error, can not find from EntityDef.moduledefs");
				return;
			}
				
			Method method = null;
			if(!module.base_methods.TryGetValue(methodname, out method))
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
			
			ScriptModule module = null;
			if(!EntityDef.moduledefs.TryGetValue(className, out module))
			{
				Dbg.ERROR_MSG("entity::cellCall:  entity-module(" + className + ") error, can not find from EntityDef.moduledefs!");
				return;
			}
			
			Method method = null;
			if(!module.cell_methods.TryGetValue(methodname, out method))
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
		public virtual Vector3 positionLocalToWorld(Vector3 localPos)
        {
			//return KBEMath.positionLocalToWorld(position, direction, localPos);
			if (renderObj != null)
				return KBEMath.positionLocalToWorld(renderObj.transform.position, renderObj.transform.eulerAngles, localPos);
			else
				return KBEMath.positionLocalToWorld(position, direction, localPos);
        }

		public virtual Vector3 positionWorldToLocal(Vector3 worldPos)
        {
			if (renderObj != null)
				return KBEMath.positionWorldToLocal(renderObj.transform.position, renderObj.transform.eulerAngles, worldPos);
			else
				return KBEMath.positionWorldToLocal(position, direction, worldPos);
        }

		public virtual Vector3 directionLocalToWorld(Vector3 localDir)
        {
			if (renderObj != null)
				return KBEMath.directionLocalToWorld(renderObj.transform.eulerAngles, localDir);
			else
				return KBEMath.directionLocalToWorld(direction, localDir);
        }

		public virtual Vector3 directionWorldToLocal(Vector3 worldDir)
        {
			if (renderObj != null)
				return KBEMath.directionWorldToLocal(renderObj.transform.eulerAngles, worldDir);
			else
				return KBEMath.directionWorldToLocal(direction, worldDir);
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

		internal void addChild(Entity ent)
        {
            children.Add(ent.id, ent);
        }

		internal void removeChild(Entity ent)
        {
            children.Remove(ent.id);
        }

		internal Entity getChild(Int32 eid)
        {
            Entity entity = null;
            children.TryGetValue(eid, out entity);
            return entity;
        }

		public void syncVolatileDataToChildren(bool positionOnly)
        {
            if (children.Count == 0)
                return;

			foreach (KeyValuePair<Int32, Entity> dic in children)
			{
				Entity ent = dic.Value;

				// 父对象的朝向改变会引发子对象的世界坐标的改变
				ent.position = positionLocalToWorld(ent.localPosition);

				// 设置最后更新值，以避免被控制者向服务器发送世界坐标或朝向
				ent._entityLastLocalPos = ent.position;

				if (!positionOnly)
				{
					// 更新世界朝向
					ent.direction = directionLocalToWorld(ent.localPosition);

					// 设置最后更新值，以避免被控制者向服务器发送世界坐标或朝向
					ent._entityLastLocalDir = ent.direction;
				}

				// 对于玩家自已或被本机控制的entity而言，因父对象的移动而移动，
				// 新坐标不需要通知服务器，因为每个客户端都会做同样的处理，服务器也会自行计算。
				if (ent.isPlayer() || ent.isControlled)
				{
					ent._entityLastLocalPos = ent.position;
					if (!positionOnly)
						ent._entityLastLocalDir = ent.direction;
				}
			}
        }

		/// <summary>
		/// 通知子对象更新自己的易变数据——即坐标和朝向。
		/// 此方法有必要时可以由外部逻辑调用。
		/// </summary>
		public void syncAndNotifyVolatileDataToChildren(bool positionOnly)
		{
			syncVolatileDataToChildren(positionOnly);
			foreach (KeyValuePair<Int32, Entity> dic in children)
			{
				dic.Value.onUpdateVolatileDataByParent();
			}
		}

		/// <summary>
		/// 内部接口，用于引擎强制设置entity的坐标
		/// </summary>
		/// <param name="pos"></param>
		internal void setPositionFromServer(Vector3 pos)
		{
			Vector3 old = position;
			position = _entityLastLocalPos = pos;

			if (isPlayer())
				KBEngineApp.app.entityServerPos(position);

			if (parent != null)
				localPosition = parent.positionWorldToLocal(position);
			else
				localPosition = position;

			syncVolatileDataToChildren(true);
			set_position(old);
		}

		/// <summary>
		/// 内部接口，用于引擎强制设置entity的朝向
		/// </summary>
		/// <param name="dir"></param>
		internal void setDirectionFromServer(Vector3 dir)
		{
			Vector3 old = direction;
			direction = _entityLastLocalDir = dir;

			if (parent != null)
				localDirection = parent.directionWorldToLocal(direction);
			else
				localDirection = direction;

			syncVolatileDataToChildren(false);
			set_direction(old);
		}

		/// <summary>
		/// 用于被控制者（如角色）定期向服务器更新其世界坐标和世界朝向
		/// </summary>
		public void updateVolatileDataToServer(Vector3 pos, Vector3 dir)
		{
			bool posChanged = false;
			bool dirChanged = false;

			if (Vector3.Distance(position, pos) > 0.001f)
			{
				position = pos;
				posChanged = true;

				if (parent != null)
					localPosition = parent.positionWorldToLocal(position);
				else
					localPosition = position;
			}

			if (Vector3.Distance(direction, dir) > 0.001f)
			{
				direction = dir;
				dirChanged = true;

				if (parent != null)
					localDirection = parent.directionWorldToLocal(direction);
				else
					localDirection = direction;
			}

			if (dirChanged)
				syncAndNotifyVolatileDataToChildren(false);  // 父的朝向改变会同时计算子对象的朝向和位置，所以需要先判断
			else if (posChanged)
				syncAndNotifyVolatileDataToChildren(true);
		}

    }

}
