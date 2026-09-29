using UnityEngine;

/// <summary>
/// Plays a flesh slap when this item hits the container's floor or walls. Contacts with other
/// items are left to the merge sound; resting, rolling, and sliding contacts stay silent because
/// only the speed along the contact normal counts.
/// </summary>
[RequireComponent(typeof(MergeItem))]
public class FleshImpactSound : MonoBehaviour
{
    [Tooltip("Speed into the surface (m/s) below which a contact is silent.")]
    [SerializeField] private float minImpactSpeed = 0.8f;

    [Tooltip("Speed into the surface (m/s) that plays at full impact volume.")]
    [SerializeField] private float maxImpactSpeed = 7f;

    [Tooltip("Seconds before this item can slap again, so a bounce does not rattle.")]
    [SerializeField] private float cooldown = 0.15f;

    private MergeItem item;
    private float nextAllowedTime;

    private void Awake()
    {
        item = GetComponent<MergeItem>();
    }

    private void OnEnable()
    {
        // Pooled items come back with the previous life's cooldown.
        nextAllowedTime = 0f;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (item.IsConsumed || Time.time < nextAllowedTime || collision.contactCount == 0)
        {
            return;
        }

        if (collision.rigidbody != null && collision.rigidbody.TryGetComponent(out MergeItem _))
        {
            return;
        }

        ContactPoint contact = collision.GetContact(0);
        float impactSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
        if (impactSpeed < minImpactSpeed)
        {
            return;
        }

        nextAllowedTime = Time.time + cooldown;
        float strength = Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, impactSpeed);
        AudioManager.PlayImpact(contact.point, strength, item.Radius);
    }
}
