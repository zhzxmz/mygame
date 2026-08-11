using UnityEngine;

public class Attack : MonoBehaviour
{
    public int attackPower;
    public int finalDamage;

    public void DoAttack(Health target)
    {
        CharacterState stats = target.GetComponent<CharacterState>();

        finalDamage = attackPower;

        if (stats != null)
        {
            finalDamage -= (int)stats.defense;
            if (finalDamage < 0) finalDamage = 0;
        }

        target.TakeDamage(finalDamage);
    }
}