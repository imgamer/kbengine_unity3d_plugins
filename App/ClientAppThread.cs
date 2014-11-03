using UnityEngine;
using System;
using System.Collections;
using KBEngine;

public class ClientAppThread : MonoBehaviour 
{
	public static KBEngineAppThread gameapp = null;
	
	void Awake() 
	 {
		DontDestroyOnLoad(transform.gameObject);
	 }
 
	// Use this for initialization
	void Start () 
	{
		MonoBehaviour.print("clientapp::start()");
		installEvents();
		initKBEngine();
	}
	
	void installEvents()
	{
	}
	
	void initKBEngine()
	{
		gameapp = new KBEngineAppThread(Application.persistentDataPath, "127.0.0.1", 20013, 5);
	}
	
	void OnDestroy()
	{
		MonoBehaviour.print("clientapp::OnDestroy(): begin");
		KBEngineApp.app.destroy();
		MonoBehaviour.print("clientapp::OnDestroy(): over, isbreak=" + gameapp.isbreak + ", over=" + gameapp.kbethread.over);
	}
	
	void FixedUpdate () {
		KBEUpdate();
	}
		
	void KBEUpdate()
	{
		KBEngine.Event.processOutEvents();
	}
}
