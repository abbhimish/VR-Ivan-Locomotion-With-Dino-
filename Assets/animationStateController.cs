using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class animationStateController : MonoBehaviour
{

    Animator animator;

    // Start is called before the first frame update
    void Start()
    {
        animator = GetComponent<Animator>();
    }

    // Update is called once per frame
    void Update()
    {

        bool bPressed = Input.GetKey("b") || OVRInput.Get(OVRInput.RawButton.B);
        if (bPressed)
        {
            animator.SetBool("isB", true);
        }
        if (!bPressed)
        {
            animator.SetBool("isB", false);
        }

        bool aPressed = Input.GetKey("a") || OVRInput.Get(OVRInput.RawButton.A);
        if (aPressed)
        {
            animator.SetBool("isA", true);
        }
        if (!aPressed)
        {
            animator.SetBool("isA", false);
        }

        bool tPressed = Input.GetKey("t") || OVRInput.Get(OVRInput.RawButton.X);
        if (tPressed)
        {
            animator.SetBool("isThumb", true);
        }
        if (!tPressed)
        {
            animator.SetBool("isThumb", false);
        }

        bool hPressed = Input.GetKey("h") || OVRInput.Get(OVRInput.RawButton.RThumbstick);
        if (hPressed)
        {
            animator.SetBool("isHandTrig", true);
        }
        if (!hPressed)
        {
            animator.SetBool("isHandTrig", false);
        }

        bool iPressed = Input.GetKey("i") || OVRInput.Get(OVRInput.RawButton.Y);
        if (iPressed)
        {
            animator.SetBool("isIndexTrig", true);
        }
        if (!iPressed)
        {
            animator.SetBool("isIndexTrig", false);
        }


    }
}
