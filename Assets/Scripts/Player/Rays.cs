using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Rays : MonoBehaviour
{
    // Start is called before the first frame update
    

    // Update is called once per frame
    void Update()
    {
        Ray ray = new Ray(transform.position,transform.forward);
        RaycastHit hit;
        if(Physics.Raycast(ray,out hit,10f))
        {
            Debug.Log(hit.collider.gameObject.name);
        }
    }
}
