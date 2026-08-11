using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RayDestory : MonoBehaviour
{
    // Start is called before the first frame update
    public void OnRaycastHit()
    {
        Destroy(gameObject);
        //调用的接口去摧毁挂载脚本的物体
        Debug.Log("DESTORY GAMEOBJECT");
    }
}
