using UnityEngine;
using System.Collections;

public class CrashHandler : MonoBehaviour
{
    [SerializeField] private float _flashDuration = 0.1f;
    [SerializeField] private int _flashCount = 6;
    [SerializeField] private float _respawnDelay = 1f;

    private PlayerController _playerController;
    private GearSystem _gearSystem;
    private SpriteRenderer _spriteRenderer;
    private float _savedProgress;
    private bool _isCrashing;

    private void Awake()
    {
        _playerController = GetComponent<PlayerController>();
        _gearSystem = GetComponent<GearSystem>();
        _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    private void Update()
    {
        if (!_isCrashing)
        {
            _savedProgress = _playerController.SplineProgress;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log($"Hit: {other.gameObject.name} Tag: {other.tag}");

        if (_isCrashing)
        {
            return;
        }

        if (other.CompareTag("Grandstand") || other.CompareTag("Tree"))
        {
            StartCoroutine(CrashSequence());
        }
    }

    private IEnumerator CrashSequence()
    {
        _isCrashing = true;
        _playerController.SetCrashed(true);

        yield return new WaitForSeconds(_respawnDelay);

        // Flash the sprite on and off to signal respawning to the player
        for (int flashIndex = 0; flashIndex < _flashCount; flashIndex++)
        {
            _spriteRenderer.enabled = false;
            yield return new WaitForSeconds(_flashDuration);
            _spriteRenderer.enabled = true;
            yield return new WaitForSeconds(_flashDuration);
        }

        _playerController.ResetToSpline(_savedProgress, 0f);
        _gearSystem.ResetToFirstGear();
        _playerController.SetCrashed(false);
        _isCrashing = false;
    }
}
