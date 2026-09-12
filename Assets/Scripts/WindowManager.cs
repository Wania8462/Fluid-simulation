using UnityEngine;
using UnityEngine.UI;

public class WindowManager : MonoBehaviour
{
    [SerializeField] private GameObject sideMenu;
    [SerializeField] private GameObject windowPrefab;

    public void OnSideMenuClick()
    {
        sideMenu.SetActive(!sideMenu.activeSelf);
    }
}
