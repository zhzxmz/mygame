using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WeaponController : MonoBehaviour
{public Transform weaponPivot; 
    float weaponX;
    float weaponY;

    public void RotateWeapon(float mouseX, float mouseY)
    {
        if (weaponPivot == null) return;

        weaponY += mouseX;
        weaponX -= mouseY;

        weaponX = Mathf.Clamp(weaponX, -45f, 45f); // 限制上下
 weaponY = Mathf.Clamp(weaponY, -45f, 45f);
    weaponPivot.localRotation = Quaternion.Euler(weaponX, weaponY, 0f);
    }
}
