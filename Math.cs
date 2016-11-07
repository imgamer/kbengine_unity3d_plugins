using UnityEngine;
using KBEngine;
using System; 
using System.Collections;

namespace KBEngine
{

/*
	KBEngine的数学相关模块
*/
public class KBEMath 
{
	public static float int82angle(SByte angle, bool half)
	{
		float halfv = 128f;
		if(half == true)
			halfv = 254f;
		
		halfv = ((float)angle) * ((float)System.Math.PI / halfv);
		return halfv;
	}
	
	public static bool almostEqual(float f1, float f2, float epsilon)
	{
		return Math.Abs( f1 - f2 ) < epsilon;
	}

	/// <summary>
	/// KBE和U3D中，描述方向的X,Y,Z两者的对应关系是不一致的：
	///    a.KBE用弧度，U3D用角度；
	///    b.KBE用的是roll、pitch、yaw概念，U3D下直接用的是x、y、z轴。
	/// 这种不一致给外插件的使用者带来了一定的麻烦，因此我们在插件内部应该直接进行转換。
	/// 在插件层，我们直接使用U3D的概念，抹平使用障碍：
	///   a.内部发送给服务器时，转換成KBE格式发送，
	///   b.从服务器获取朝向时则转換成相U3D的格式。
	/// </summary>
	public static Vector3 Unity2KBEngineDirection(Vector3 u3dDir)
	{
		return angles2radian(u3dDir.z, u3dDir.x, u3dDir.y);
	}

	public static Vector3 Unity2KBEngineDirection(float x, float y, float z)
	{
		return angles2radian(z, x, y);
	}

	public static Vector3 KBEngine2UnityDirection(Vector3 kbeDir)
	{
		return radian2angles(kbeDir.y, kbeDir.z, kbeDir.x);
	}

	public static Vector3 KBEngine2UnityDirection(float roll_x, float pitch_y, float yaw_z)
	{
		return radian2angles(roll_x, pitch_y, yaw_z);
	}


	/// <summary>
	/// 弧度转角度
	/// </summary>
	/// <param name="v"></param>
	/// <returns></returns>
	public static float radian2angles(float v)
	{
		float result = v * 360 / ((float)System.Math.PI * 2);
		if (result < 0)
			result += 360;  // 转成0 - 360之间的角度
		return result;
	}

	public static Vector3 radian2angles(Vector3 v)
	{
		return new Vector3(radian2angles(v.x), radian2angles(v.y), radian2angles(v.z));
	}

	public static Vector3 radian2angles(float x, float y, float z)
	{
		return new Vector3(radian2angles(x), radian2angles(y), radian2angles(z));
	}

	/// <summary>
	/// 角度转弧度
	/// </summary>
	/// <param name="v"></param>
	/// <returns></returns>
	public static float angles2radian(float v)
	{
		float r = v / 360 * ((float)System.Math.PI * 2);
		// 根据弧度转角度公式会出现负数
		// unity会自动转化到0~360度之间，这里需要做一个还原
		if (r - (float)System.Math.PI > 0.0)
			r -= (float)System.Math.PI * 2;
		return r;
	}

	public static Vector3 angles2radian(Vector3 v)
	{
		return new Vector3(angles2radian(v.x), angles2radian(v.y), angles2radian(v.z));
	}

	public static Vector3 angles2radian(float x, float y, float z)
	{
		return new Vector3(angles2radian(x), angles2radian(y), angles2radian(z));
	}


}


}
