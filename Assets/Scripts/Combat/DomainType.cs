// Which sea domain a ship, weapon, or vision layer operates in.
// "Both" is used for weapons/sensors that are domain-agnostic (e.g. a sensor
// that detects surface AND sub-surface targets, or a vision layer active in either state).
public enum DomainType
{
    Surface,
    SubSurface,
    Both
}
