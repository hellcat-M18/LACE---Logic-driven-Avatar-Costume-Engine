using UnityEngine;
using VRC.SDKBase;

namespace Lace.Runtime
{
    /// <summary>
    /// Expression Menu のサブメニューフォルダを定義するコンポーネント。
    /// GameObject の名前がメニュー表示名になります。
    /// ヒエラルキー上のネスト（LaceMenuFolder の子に LaceMenuFolder）でサブメニュー階層を構築できます。
    /// ビルド時に Modular Avatar の SubMenu として非破壊で生成されます。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("LACE/LACE Menu Folder")]
    public class LaceMenuFolder : MonoBehaviour, IEditorOnly
    {
    }
}
