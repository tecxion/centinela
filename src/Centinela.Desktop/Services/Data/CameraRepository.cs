using Centinela.Core;

namespace Centinela.Desktop.Services.Data;

/// <summary>The camera list in memory and on disk. UI thread only.</summary>
sealed class CameraRepository(CameraStore store)
{
    readonly List<Camera> _cameras = [];

    public IReadOnlyList<Camera> All => _cameras;

    /// <summary>Raised after a successful save; listeners must catch their own errors (the automatic backup does).</summary>
    public event Action? Saved;
    /// <summary>Raised with a Spanish message when saving fails.</summary>
    public event Action<string>? SaveFailed;

    public void Load()
    {
        _cameras.Clear();
        _cameras.AddRange(store.Load());
        for (var i = 0; i < _cameras.Count; i++) _cameras[i].Order = i;
    }

    public Camera? Find(Guid id) => _cameras.FirstOrDefault(c => c.Id == id);

    /// <summary>The stored camera with this Id (a view may hold an older copy); the given one if it was deleted.</summary>
    public Camera CurrentOr(Camera shown) => Find(shown.Id) ?? shown;

    public void Add(Camera camera)
    {
        camera.Order = _cameras.Count == 0 ? 0 : _cameras.Max(c => c.Order) + 1;
        _cameras.Add(camera);
    }

    public void Replace(Camera updated)
    {
        var index = _cameras.FindIndex(c => c.Id == updated.Id);
        if (index >= 0) _cameras[index] = updated;
    }

    public void Remove(Guid id) => _cameras.RemoveAll(c => c.Id == id);

    public bool Save()
    {
        try
        {
            store.Save(_cameras);
        }
        catch (Exception ex)
        {
            SaveFailed?.Invoke($"No se pudieron guardar las cámaras: {ex.Message}");
            return false;
        }
        Saved?.Invoke();
        return true;
    }
}
