import { useCallback, useEffect, useState } from 'react'
import type { FormEvent } from 'react'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080').replace(/\/$/, '')

type Snapshot = { id: string; commitSha: string; importedAtUtc: string }
type Conversation = { id: string; snapshotId: string; title: string; updatedAtUtc: string }
type Citation = { id: string; path: string; startLine: number; endLine: number }
type Message = { id: string; role: 'user' | 'assistant'; content: string; model: string | null; citations: Citation[]; createdAtUtc: string }
type ConversationDetail = { conversation: Conversation; messages: Message[] }
type StreamEvent = { type: 'delta' | 'complete' | 'error'; text?: string; messageId?: string; citations?: Citation[]; error?: string }
type ProblemDetails = { title?: string; detail?: string; errors?: Record<string, string[]> }

function errorMessage(problem: ProblemDetails, fallback: string) {
  return (problem.errors ? Object.values(problem.errors).flat()[0] : undefined)
    ?? problem.detail ?? problem.title ?? fallback
}

export default function ProjectChatPanel({
  projectId,
  authorization,
  onUnauthorized,
  view,
  conversationId,
  onOpenConversation,
  onBack,
}: {
  projectId: string
  authorization: string
  onUnauthorized: () => void
  view: 'list' | 'chat'
  conversationId?: string
  onOpenConversation: (conversationId: string) => void
  onBack: () => void
}) {
  const [snapshots, setSnapshots] = useState<Snapshot[]>([])
  const [conversations, setConversations] = useState<Conversation[]>([])
  const [snapshotId, setSnapshotId] = useState('')
  const [messages, setMessages] = useState<Message[]>([])
  const [question, setQuestion] = useState('')
  const [loading, setLoading] = useState(true)
  const [sending, setSending] = useState(false)
  const [error, setError] = useState('')

  const reload = useCallback(async (signal?: AbortSignal) => {
    try {
      const headers = { Authorization: authorization }
      const [snapshotResponse, conversationResponse] = await Promise.all([
        fetch(`${apiBaseUrl}/api/v1/projects/${projectId}/snapshots`, { headers, signal }),
        fetch(`${apiBaseUrl}/api/v1/projects/${projectId}/conversations`, { headers, signal }),
      ])
      if (snapshotResponse.status === 401 || conversationResponse.status === 401) {
        onUnauthorized()
        return
      }
      if (!snapshotResponse.ok || !conversationResponse.ok) {
        const failedResponse = !snapshotResponse.ok ? snapshotResponse : conversationResponse
        let problem: ProblemDetails = {}
        try { problem = await failedResponse.json() as ProblemDetails } catch { /* non-JSON proxy response */ }
        throw new Error(errorMessage(problem, 'Could not load repository chats.'))
      }
      const snapshotData = await snapshotResponse.json() as Snapshot[]
      const conversationData = await conversationResponse.json() as Conversation[]
      setError('')
      setSnapshots(snapshotData)
      setConversations(conversationData)
      setSnapshotId((current) => snapshotData.some((snapshot) => snapshot.id === current)
        ? current : snapshotData[0]?.id ?? '')
      if (view === 'chat' && conversationId && !conversationData.some((conversation => conversation.id === conversationId))) {
        setError('This conversation could not be found for the selected repository.')
      }
    } catch (cause) {
      if (cause instanceof DOMException && cause.name === 'AbortError') return
      setError(cause instanceof Error ? cause.message : 'Could not load repository chats.')
    } finally {
      setLoading(false)
    }
  }, [authorization, conversationId, onUnauthorized, projectId, view])

  useEffect(() => {
    const controller = new AbortController()
    // The loader updates state only after its network requests settle; the signal
    // ensures those async updates are abandoned if the project card unmounts.
    // oxlint-disable-next-line react/set-state-in-effect
    void reload(controller.signal)
    return () => controller.abort()
  }, [reload])

  useEffect(() => {
    if (view !== 'chat' || !conversationId) return
    const controller = new AbortController()
    fetch(`${apiBaseUrl}/api/v1/projects/${projectId}/conversations/${conversationId}`, {
      headers: { Authorization: authorization }, signal: controller.signal,
    }).then(async (response) => {
      if (response.status === 401) { onUnauthorized(); return null }
      if (!response.ok) throw new Error('Could not reopen this conversation.')
      return await response.json() as ConversationDetail
    }).then((detail) => { if (detail) setMessages(detail.messages) })
      .catch((cause: unknown) => {
        if (!(cause instanceof DOMException && cause.name === 'AbortError')) {
          setError(cause instanceof Error ? cause.message : 'Could not reopen this conversation.')
        }
      })
    return () => controller.abort()
  }, [authorization, conversationId, onUnauthorized, projectId, view])

  async function createConversation() {
    if (!snapshotId) return
    setError('')
    try {
      const response = await fetch(`${apiBaseUrl}/api/v1/projects/${projectId}/conversations`, {
        method: 'POST',
        headers: { Authorization: authorization, 'Content-Type': 'application/json' },
        body: JSON.stringify({ snapshotId }),
      })
      if (response.status === 401) { onUnauthorized(); return }
      if (!response.ok) throw new Error('Could not create a conversation for this snapshot.')
      const conversation = await response.json() as Conversation
      setConversations((current) => [conversation, ...current])
      onOpenConversation(conversation.id)
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : 'Could not create a conversation.')
    }
  }

  async function sendQuestion(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (view !== 'chat' || !conversationId || !question.trim() || sending) return
    const submitted = question.trim()
    const temporaryUserId = `local-user-${crypto.randomUUID()}`
    const temporaryAssistantId = `local-assistant-${crypto.randomUUID()}`
    setQuestion('')
    setError('')
    setSending(true)
    setMessages((current) => [
      ...current,
      { id: temporaryUserId, role: 'user', content: submitted, model: null, citations: [], createdAtUtc: new Date().toISOString() },
      { id: temporaryAssistantId, role: 'assistant', content: '', model: null, citations: [], createdAtUtc: new Date().toISOString() },
    ])
    try {
      const response = await fetch(`${apiBaseUrl}/api/v1/projects/${projectId}/conversations/${conversationId}/messages/stream`, {
        method: 'POST',
        headers: { Authorization: authorization, 'Content-Type': 'application/json', Accept: 'text/event-stream' },
        body: JSON.stringify({ content: submitted }),
      })
      if (response.status === 401) { onUnauthorized(); throw new Error('Please sign in again.') }
      if (!response.ok || !response.body) {
        let problem: ProblemDetails = {}
        try { problem = await response.json() as ProblemDetails } catch { /* response may not be JSON */ }
        throw new Error(errorMessage(problem, 'Could not start the answer stream.'))
      }
      const reader = response.body.getReader()
      const decoder = new TextDecoder()
      let pending = ''
      let eventType = ''
      let data = ''
      let completedMessageId: string | undefined
      let completedCitations: Citation[] = []
      const consume = (block: string) => {
        for (const line of block.split('\n')) {
          if (line.startsWith('event:')) eventType = line.slice(6).trim()
          else if (line.startsWith('data:')) data += `${line.slice(5).trim()}\n`
        }
        if (!data) { eventType = ''; return }
        const item = JSON.parse(data.trim()) as StreamEvent
        if (eventType === 'delta' && item.text) {
          setMessages((current) => current.map((message) => message.id === temporaryAssistantId
            ? { ...message, content: message.content + item.text } : message))
        } else if (eventType === 'complete') {
          completedMessageId = item.messageId
          completedCitations = item.citations ?? []
        } else if (eventType === 'error') {
          throw new Error(item.error ?? 'The model could not answer this question.')
        }
        eventType = ''
        data = ''
      }
      while (true) {
        const { value, done } = await reader.read()
        pending += decoder.decode(value, { stream: !done }).replace(/\r\n/g, '\n')
        let delimiter = pending.indexOf('\n\n')
        while (delimiter >= 0) {
          consume(pending.slice(0, delimiter))
          pending = pending.slice(delimiter + 2)
          delimiter = pending.indexOf('\n\n')
        }
        if (done) break
      }
      if (completedMessageId) {
        setMessages((current) => current.map((message) => message.id === temporaryAssistantId
          ? { ...message, id: completedMessageId!, citations: completedCitations } : message))
        void reload()
      }
    } catch (cause) {
      setMessages((current) => current.filter((message) => message.id !== temporaryAssistantId))
      setError(cause instanceof Error ? cause.message : 'The answer could not be generated.')
    } finally {
      setSending(false)
    }
  }

  return (
    <section className={`repository-chat-panel ${view === 'chat' ? 'chat-page-panel' : 'conversation-library-panel'}`} aria-label={view === 'chat' ? 'Repository chat' : 'Repository conversations'}>
      <div className="repository-chat-heading">
        <div>
          {view === 'chat' && <button className="chat-back-button" type="button" onClick={onBack}>← All conversations</button>}
          <span className="section-kicker">{view === 'chat' ? 'REPOSITORY CONVERSATION' : 'SAVED CHATS'}</span>
          <h3>{view === 'chat' ? conversations.find((item) => item.id === conversationId)?.title ?? 'Conversation' : 'Conversations'}</h3>
        </div>
        <button className="snapshot-refresh" type="button" onClick={() => {
          setLoading(true)
          setError('')
          void reload()
        }} disabled={loading}>Refresh</button>
      </div>
      {loading && <p className="chat-hint">Loading {view === 'chat' ? 'conversation' : 'repository conversations'}…</p>}
      {!loading && snapshots.length === 0 && view === 'list' && <p className="chat-hint">Import this repository first. A chat is grounded in a completed repository snapshot.</p>}
      {view === 'list' && snapshots.length > 0 && (
        <div className="repository-chat-controls">
          <label>Snapshot
            <select value={snapshotId} onChange={(event) => setSnapshotId(event.target.value)} disabled={sending}>
              {snapshots.map((snapshot) => <option key={snapshot.id} value={snapshot.id}>
                {snapshot.commitSha.slice(0, 10)} · {new Date(snapshot.importedAtUtc).toLocaleString()}
              </option>)}
            </select>
          </label>
          <button className="chat-new-button" type="button" onClick={() => void createConversation()} disabled={!snapshotId}>New conversation</button>
        </div>
      )}
      {error && <p className="chat-error" role="alert">{error}</p>}
      {view === 'list' && conversations.length > 0 && (
        <div className="conversation-list" aria-label="Saved conversations">
          {conversations.map((conversation) => (
            <button className="conversation-list-card" type="button" key={conversation.id} onClick={() => onOpenConversation(conversation.id)}>
              <span className="conversation-list-icon"><span>↗</span></span>
              <span className="conversation-list-copy"><strong>{conversation.title}</strong><small>Updated {new Date(conversation.updatedAtUtc).toLocaleString()}</small></span>
              <span className="conversation-list-arrow">→</span>
            </button>
          ))}
        </div>
      )}
      {view === 'list' && !loading && snapshots.length > 0 && conversations.length === 0 && (
        <div className="conversation-empty-state"><strong>No conversations yet</strong><span>Choose a snapshot above and start a repository-grounded chat.</span></div>
      )}
      {view === 'chat' && conversationId && (
        <>
          <div className="chat-message-list" aria-live="polite">
            {messages.map((message) => (
              <article className={`chat-message ${message.role}`} key={message.id}>
                <strong>{message.role === 'user' ? 'You' : 'RepoAI'}</strong>
                <p>{message.content || (sending ? 'Thinking…' : '')}</p>
                {message.citations.length > 0 && <div className="chat-citations">
                  {message.citations.map((citation) => <span key={citation.id} title={`${citation.path}:${citation.startLine}-${citation.endLine}`}>
                    [{citation.id}] {citation.path}:{citation.startLine}-{citation.endLine}
                  </span>)}
                </div>}
              </article>
            ))}
          </div>
          <form className="repository-chat-form" onSubmit={sendQuestion}>
            <textarea value={question} onChange={(event) => setQuestion(event.target.value)} maxLength={8000}
              placeholder="Ask how this repository is structured, where a request flows, or what a service does…" disabled={sending} required />
            <button type="submit" disabled={sending || !question.trim()}>{sending ? 'Answering…' : 'Ask RepoAI'}</button>
          </form>
          <p className="chat-hint">Answers use retrieved source excerpts and cite file paths and line ranges. Verify critical details in the source.</p>
        </>
      )}
    </section>
  )
}
