export function EmptyState({ title, message }: { title: string; message: string }) {
  return (
    <section className="empty-state reveal">
      <h2>{title}</h2>
      <p>{message}</p>
    </section>
  );
}
