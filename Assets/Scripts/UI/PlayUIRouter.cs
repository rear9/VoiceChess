using UnityEngine;

public class PlayUIRouter : MonoBehaviour
{
    [SerializeField] private GameObject desktopCanvas;
    [SerializeField] private GameObject mobileCanvas;

    private void Awake()
    {
        bool isMobile = Application.isMobilePlatform
                        || Application.platform == RuntimePlatform.Android
                        || Application.platform == RuntimePlatform.IPhonePlayer;
        if (desktopCanvas) desktopCanvas.SetActive(!isMobile);
        if (mobileCanvas) mobileCanvas.SetActive(isMobile);
    }
}
