namespace Rts
{
    public interface ISideVisibility
    {
        bool IsVisible(Side observer, CombatTarget target);
    }
}
