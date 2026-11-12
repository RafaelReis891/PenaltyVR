using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Michsky.UI.Shift
{
    public class TimedEvent : MonoBehaviour
    {
        [Header("Timing (seconds)")]
        public float timer = 4;
        public bool enableAtEnable, enableAtStart;

        [Header("Timer Event")]
        public UnityEvent timerAction;

        private void OnEnable()
        {
            if (enableAtEnable == true)
            {
                StartCoroutine(TimedEventStart());
            }
        }

        void Start()
        {
            if(enableAtStart == true)
            {
                StartCoroutine(TimedEventStart());
            }
        }

        IEnumerator TimedEventStart()
        {
            yield return new WaitForSeconds(timer);
            timerAction.Invoke();
        }

        public void StartIEnumerator ()
        {
            StartCoroutine(TimedEventStart());
        }

        public void StopIEnumerator ()
        {
            StopCoroutine(TimedEventStart());
        }
    }
}
