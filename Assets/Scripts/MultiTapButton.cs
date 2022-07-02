using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UniRx;
using UniRx.Triggers;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The button that fires an event when tapped multiple times
/// </summary>
[Serializable]
public class MultiTapButton : MonoBehaviour
{

    [Serializable]
    public struct Context
    {
        /// <summary>
        /// Number of clicks required to fire the event
        /// </summary>
        [SerializeField]
        private uint _requiredTapCount;

        public uint RequiredTapCount => _requiredTapCount;



        /// <summary>
        /// Maximum time that is considered to be repeated when the button is released and pressed again
        /// </summary>
        [SerializeField]
        private float _maxTapSpacing;

        public float MaxTapSpacing => _maxTapSpacing;



        /// <summary>
        /// Maximum time that is considered a long press
        /// </summary>
        [SerializeField]
        private float _maxTapDuration;

        public float MaxTapDuration => _maxTapDuration;



        /// <summary></summary>
        /// <param name="requredCount">set as <see cref="RequiredTapCount"/></param>
        /// <param name="maxSpacing">set as <see cref="MaxTapSpacing"/></param>
        /// <param name="maxDuration">set as <see cref="MaxTapDuration"/></param>
        public Context(uint requredCount, float maxSpacing, float maxDuration)
        {
            _requiredTapCount = (uint)Mathf.Clamp(requredCount, uint.MinValue + 1, uint.MaxValue);
            _maxTapSpacing = Mathf.Clamp(maxSpacing, float.Epsilon, float.MaxValue);
            _maxTapDuration = Mathf.Clamp(maxDuration, float.Epsilon, float.MaxValue);
        }
    }



    /// <summary></summary>
    [SerializeField]
    private Context _context;



    /// <summary>
    /// initial value of <see cref="_count"/>
    /// </summary>
    private const int INITIAL_COUNT = 0;



    /// <summary>
    /// Number of times already tapped
    /// </summary>
    private int _count = INITIAL_COUNT;



    /// <summary>
    /// Image of Button
    /// </summary>
    [SerializeField]
    protected Image ButtonImage;


    /// <summary>
    /// Text on button
    /// </summary>
    [SerializeField]
    protected Text Text;



    /// <summary>
    /// root button
    /// </summary>
    [SerializeField]
    protected ObservableEventTrigger EventTrigger;



    /// <summary>
    /// Event fired by a single tap
    /// </summary>
    public IObservable<PointerEventData> OnTapAsObservable => EventTrigger.OnPointerClickAsObservable();



    // MEMO: GetCancellationTokenOnDestroyからCancelリクエストが来るよりも、
    //       OnDestroy()が呼ばれる方が早い。
    //       そのため、CancellationTokenSource, CancellationTokenOnDestroyを
    //       自前で作成し、SubjectのDisposeよりも早い段階でCancelが行われるようにする必要がある。
    /// <summary>
    /// when <see cref="FixedCountObservable{T}"/> is disposed,
    /// The Token obtained from this Source is cancelled.
    /// Then this Source is disposed.
    /// </summary>
    private CancellationTokenSource _cts = new CancellationTokenSource();



    /// <summary>
    /// Property of Token that obtained from <see cref="_cts"/>.
    /// </summary>
    private CancellationToken CancellationTokenOnDestroy => _cts.Token;



    /// <summary>
    /// 外部に公開する<see cref="Subject{T}"/>
    /// </summary>
    private Subject<PointerEventData> _multiTapSubject = new Subject<PointerEventData>();



    /// <summary>
    /// Event fired by tapping a specified number of times
    /// </summary>
    public IObservable<PointerEventData> OnMultiTapAsObservable => _multiTapSubject;



    private void Awake()
    {
        if (ButtonImage == null)
        {
            ButtonImage = GetComponent<Image>();
        }
        if (EventTrigger == null && !TryGetComponent<ObservableEventTrigger>(out EventTrigger))
        {
            EventTrigger = gameObject.AddComponent<ObservableEventTrigger>();
        }

        _cts.AddTo(EventTrigger);
        ObserveMultiTap(PlayerLoopTiming.Update, CancellationTokenOnDestroy).Forget();
    }



    private void OnDestroy()
    {
        try
        {
            // 先にCancellationTokenSourceにキャンセル命令をしてから
            _cts?.Cancel();
            // Subjectの破棄を行う。
            _multiTapSubject?.OnCompleted();
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _multiTapSubject?.Dispose();
            _multiTapSubject = null;
        }
    }



    private async UniTaskVoid ObserveMultiTap(
        PlayerLoopTiming timing = PlayerLoopTiming.Update,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_count <= INITIAL_COUNT)
            {
                var pointerEventData = await ObservePointerClickForMaxTapDuration(timing, cancellationToken);
                _count = pointerEventData != null ? INITIAL_COUNT + 1 : INITIAL_COUNT;
                continue;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                PointerEventData pointerEventData = null;
                pointerEventData = await ObservePointerDownForMaxTapSpacing(timing, cancellationToken);

                if (pointerEventData == null)
                {
                    _count = INITIAL_COUNT;
                    break;
                }

                pointerEventData = await ObservePointerUpForMaxTapDuration(timing, cancellationToken);

                if (pointerEventData == null)
                {
                    _count = INITIAL_COUNT;
                    break;
                }
                else if (++_count >= _context.RequiredTapCount)
                {
                    _count = INITIAL_COUNT;
                    _multiTapSubject?.OnNext(pointerEventData);
                    break;
                }

                continue;
            }
        }
    }



    private async UniTask<PointerEventData> ObservePointerClickForMaxTapDuration(
        PlayerLoopTiming timing = PlayerLoopTiming.Update,
        CancellationToken cancellationToken = default)
    {
        // OnNextから流れてくる値を待ち受けるかどうか
        bool useFirstValue = true;

        await EventTrigger.OnPointerDownAsObservable().ToUniTask(useFirstValue, cancellationToken);

        var (hasResultLeft, result) = await UniTask.WhenAny(
                EventTrigger.OnPointerUpAsObservable().ToUniTask(useFirstValue, cancellationToken),
                UniTask.Delay(TimeSpan.FromSeconds(_context.MaxTapDuration), delayTiming: timing, cancellationToken: cancellationToken)
                );

        return hasResultLeft ? result : null;
    }



    private async UniTask<PointerEventData> ObservePointerDownForMaxTapSpacing(
        PlayerLoopTiming timing = PlayerLoopTiming.Update,
        CancellationToken cancellationToken = default)
    {
            // OnNextから流れてくる値を待ち受けるかどうか
            bool useFirstValue = true;

        var (hasResultLeft, result) = await UniTask.WhenAny(
                    EventTrigger.OnPointerDownAsObservable().ToUniTask(useFirstValue, cancellationToken),
                    UniTask.Delay(TimeSpan.FromSeconds(_context.MaxTapSpacing), delayTiming: timing, cancellationToken: cancellationToken)
                    );

        return hasResultLeft ? result : null;
    }



    private async UniTask<PointerEventData> ObservePointerUpForMaxTapDuration(
        PlayerLoopTiming timing = PlayerLoopTiming.Update,
        CancellationToken cancellationToken = default)
    {
        // OnNextから流れてくる値を待ち受けるかどうか
        bool useFirstValue = true;

        var (hasResultLeft, result) = await UniTask.WhenAny(
                EventTrigger.OnPointerUpAsObservable().ToUniTask(useFirstValue, cancellationToken),
                UniTask.Delay(TimeSpan.FromSeconds(_context.MaxTapDuration), delayTiming: timing, cancellationToken: cancellationToken)
                );

        return hasResultLeft ? result : null;
    }
}
