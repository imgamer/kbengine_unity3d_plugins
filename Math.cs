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

	// 世界坐标和本地坐标互換
	public static Vector3 positionLocalToWorld(Vector3 parentPos, Vector3 parentDir, Vector3 localPos)
	{
		Quaternion p_local = new Quaternion(localPos.x, localPos.y, localPos.z, 0);

		Quaternion qx_r = Quaternion.AngleAxis(parentDir.x, new Vector3(1, 0, 0));
		Quaternion qy_r = Quaternion.AngleAxis(parentDir.y, new Vector3(0, 1, 0));
		Quaternion qz_r = Quaternion.AngleAxis(parentDir.z, new Vector3(0, 0, 1));

		Quaternion q_r = qy_r * qx_r * qz_r; //欧拉旋转的旋转顺序是Z、X、Y，不同的旋转顺序方向，需要在这里修改，Z是最上层,qy*qx*qz，从右向左
		Quaternion q_rr = Quaternion.Inverse(q_r); //逆运算
		Quaternion p = q_r * p_local * q_rr; //p经过q_r四元数旋转得到p0，所以p=q*p0*q^-1

		return new Vector3(p.x + parentPos.x, p.y + parentPos.y, p.z + parentPos.z);
	}

	public static Vector3 positionWorldToLocal(Vector3 parentPos, Vector3 parentDir, Vector3 worldPos)
	{
		Quaternion qx_r = Quaternion.AngleAxis(parentDir.x, new Vector3(1, 0, 0));
		Quaternion qy_r = Quaternion.AngleAxis(parentDir.y, new Vector3(0, 1, 0));
		Quaternion qz_r = Quaternion.AngleAxis(parentDir.z, new Vector3(0, 0, 1));

		Quaternion q_r = qy_r * qx_r * qz_r; //欧拉旋转的旋转顺序是Z、X、Y，不同的旋转顺序方向，需要在这里修改，Z是最上层,qy*qx*qz，从右向左
		Quaternion q_rr = Quaternion.Inverse(q_r); //逆运算

		Vector3 g_pos = new Vector3(worldPos.x - parentPos.x, worldPos.y - parentPos.y, worldPos.z - parentPos.z);
		Quaternion g_q = new Quaternion(g_pos.x, g_pos.y, g_pos.z, 0);
		Quaternion p_local = q_rr * g_q * q_r;

		return new Vector3(p_local.x, p_local.y, p_local.z);
	}

	public static Vector3 directionLocalToWorld(Vector3 parentDir, Vector3 localDir)
	{
		Quaternion q_parentdir = Quaternion.Euler(parentDir);
		Quaternion q_childdir = Quaternion.Euler(localDir);

		Quaternion wr = q_parentdir * q_childdir;
		Vector3 result = wr.eulerAngles;

		return result;
	}

	public static Vector3 directionWorldToLocal(Vector3 parentDir, Vector3 worldDir)
	{
		Quaternion q_parentdir = Quaternion.Euler(parentDir);
		Quaternion q_childworlddir = Quaternion.Euler(worldDir);

		Quaternion pr_r = Quaternion.Inverse(q_parentdir); //逆运算
		Quaternion lr = pr_r * q_childworlddir;

		Vector3 result = lr.eulerAngles;

		return result;
	}

}


}
