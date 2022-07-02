using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UniRx;

public class MultiTapButtonPresenter : MonoBehaviour
{
    [SerializeField]
    private MultiTapButton _multiTapButton;

    // Start is called before the first frame update
    void Start()
    {
        _multiTapButton
            .OnTapAsObservable
            .Subscribe(_ => Debug.Log("tapped !"));

        _multiTapButton
            .OnMultiTapAsObservable
            .Subscribe(_ => Debug.Log("multi tapped !"));
    }
}
