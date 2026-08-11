using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class PlayerInputController : MonoBehaviour
{
    
        
    
    
    public CameraController cameraController;
    public WeaponController weaponController;
    float mouseSensitivity=800f;
    
    void Update()
    {float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * Time.deltaTime;
    float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * Time.deltaTime;


        if (!Input.GetMouseButton(0))
        {
            cameraController.RotateCamera(mouseX, mouseY);
        }

        else
        {
            weaponController.RotateWeapon(mouseX, mouseY);
        }
            
    }
}
