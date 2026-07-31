namespace ImageCleanup.Core.Organization;

/// <summary>
/// Mutable, UI-framework-free selection wrapper around an
/// <see cref="OrganizationTreeNode"/> tree — implements standard cascading
/// tree-checkbox semantics without any WinUI dependency, so the logic is
/// unit-testable independently of the TreeView control. Built once per
/// OrganizationTreeNode tree (mirrors its shape 1:1) and kept alongside it;
/// the App layer's OrganizationNodeViewModel wraps an instance of this
/// rather than reimplementing the cascade logic itself.
/// <para>
/// Only File-kind nodes store their own selection flag. Every other kind
/// (Year/Month/Category) derives its selection state live from its
/// children instead of storing one — checking/unchecking a group node
/// cascades the same boolean down to every descendant File node
/// (<see cref="SetSelected"/>), while a group node's own displayed state
/// (<see cref="IsSelected"/>/<see cref="IsIndeterminate"/>) always reflects
/// whatever its children currently are. That means deselecting a single
/// file under a Category never has to "reach up" and explicitly change the
/// Category's stored state — there is no such state to change; the
/// Category simply computes itself as Indeterminate the next time it's
/// read, which is what makes the two directions (cascade down / recompute
/// up) trivially consistent.
/// </para>
/// </summary>
public sealed class OrganizationSelectionNode
{
    private bool _isSelected = true;

    public OrganizationTreeNode Node { get; }
    public IReadOnlyList<OrganizationSelectionNode> Children { get; }

    public OrganizationSelectionNode(OrganizationTreeNode node)
    {
        Node     = node;
        Children = node.Children.Select(c => new OrganizationSelectionNode(c)).ToList();
    }

    /// <summary>
    /// For a File node, this file's own selection flag. For any other kind,
    /// derived: true only when every descendant File is selected (vacuously
    /// true for a childless group, which shouldn't occur in practice but
    /// shouldn't render as "excluded" either).
    /// </summary>
    public bool IsSelected =>
        Node.Kind == OrganizationNodeKind.File
            ? _isSelected
            : Children.Count == 0 || Children.All(c => c.IsSelected);

    /// <summary>
    /// True when this group node has a mix of selected and unselected
    /// content beneath it — the tri-state CheckBox's "partially checked"
    /// visual. Always false for File nodes (a single file is never partial).
    /// </summary>
    public bool IsIndeterminate =>
        Node.Kind != OrganizationNodeKind.File
        && Children.Count > 0
        && !IsSelected
        && Children.Any(c => c.IsSelected || c.IsIndeterminate);

    /// <summary>Tri-state value for a ThreeState CheckBox: null (indeterminate), otherwise the definitive checked/unchecked value.</summary>
    public bool? CheckBoxState => IsIndeterminate ? null : IsSelected;

    /// <summary>
    /// Sets this node's selection. On a File node, sets just that file. On
    /// any group node, cascades the same value to every descendant File —
    /// checking/unchecking a Year/Month/Category selects/deselects
    /// everything under it. Never touches ancestors: an ancestor's own
    /// IsSelected/IsIndeterminate is always derived on read, not stored, so
    /// there is nothing to cascade upward.
    /// </summary>
    public void SetSelected(bool selected)
    {
        if (Node.Kind == OrganizationNodeKind.File)
        {
            _isSelected = selected;
            return;
        }

        foreach (var child in Children)
            child.SetSelected(selected);
    }

    /// <summary>Every currently-selected File-kind node in this subtree (leaves only) — the set OrganizationExecutor should actually move.</summary>
    public IEnumerable<OrganizationTreeNode> SelectedFileNodes() =>
        Node.Kind == OrganizationNodeKind.File
            ? (IsSelected ? [Node] : Enumerable.Empty<OrganizationTreeNode>())
            : Children.SelectMany(c => c.SelectedFileNodes());
}
