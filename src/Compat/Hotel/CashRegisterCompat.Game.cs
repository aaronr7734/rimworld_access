namespace RimWorldAccess
{
    /// <summary>
    /// Compat for CashRegister, the shared register and shift framework underneath Gastronomy and
    /// Storefront. Its Gizmo_Radius is a Gizmo_ModifyNumber copy, served by
    /// <see cref="ModifyNumberGizmoHandler"/>.
    /// </summary>
    internal static class CashRegisterCompat
    {
        public static void RegisterGizmoHandlers()
        {
            CompatRegistration.GizmoHandler("CashRegister.Gizmo_Radius",
                t => new ModifyNumberGizmoHandler(t, ModifyNumberGizmoHandler.Kind.Radius));
        }
    }
}
