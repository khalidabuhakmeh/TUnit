namespace TUnit.Assertions.AssertConditions.Operators;

public interface IOr<TActual, TAnd, TOr>
    where TAnd : IAnd<TActual, TAnd, TOr>
    where TOr : IOr<TActual, TAnd, TOr>
{
    public static abstract TOr Create(BaseAssertCondition<TActual, TAnd, TOr> otherAssertCondition);
}