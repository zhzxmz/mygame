using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class exercise : MonoBehaviour
{
    class Chest
    {
        public int number=10;
        void OpenChest()
    {
        
        if (number > 0)
            {
                Debug.Log("the chest has been opened");
                number=1;
               Debug.Log(number);
                
            }
            else
            {
                Debug.Log("the chest is empty");
            }
    }
    }
    
}
