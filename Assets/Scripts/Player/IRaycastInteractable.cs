using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    //这是一个射线检测的接口
    public interface IRaycastInteractable
{
    void OnRaycastHit();  
}
}
