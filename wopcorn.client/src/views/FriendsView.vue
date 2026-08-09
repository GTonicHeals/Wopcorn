<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';

import BaseButton from '@/components/BaseButton.vue';
import BaseSheet from '@/components/BaseSheet.vue';
import EmptyState from '@/components/EmptyState.vue';
import ErrorState from '@/components/ErrorState.vue';
import IconMore from '@/components/icons/IconMore.vue';
import IconPeople from '@/components/icons/IconPeople.vue';
import IconSearch from '@/components/icons/IconSearch.vue';
import PersonRow from '@/components/PersonRow.vue';
import ScreenHeader from '@/components/ScreenHeader.vue';
import SpinnerBlock from '@/components/SpinnerBlock.vue';
import TasteMatch from '@/components/TasteMatch.vue';
import { useUserSearch } from '@/composables/useUserSearch';
import { useFriendsStore } from '@/stores/friends';
import { useToastStore } from '@/stores/toasts';
import type { Friend, UserSearchResult } from '@/api/types';

/**
 * FR-F1…FR-F4, from one `GET /api/friends` call.
 *
 * Incoming requests come first because they are the only section that needs a
 * decision. Loading this screen is also what lights the shell's pending badge.
 *
 * The search buttons are driven by the server's `relationship` rather than
 * optimism: offering "Add friend" to someone who already sent you one earns a
 * `409` and teaches the user nothing. Where the store is fresher than the search
 * response — right after an accept, say — the store wins.
 */
const friends = useFriendsStore();
const toasts = useToastStore();

const { query, results, renderedQuery, status: searchStatus, error: searchError, runNow } =
  useUserSearch();

onMounted(() => void friends.load());

/**
 * `idle` counts as loading. The shell fetches this at boot, but on a hard
 * refresh straight onto `/friends` this view mounts first, and without it the
 * screen shows "No friends yet" for a frame before it has asked.
 */
const loadingFriends = computed(
  () => friends.status === 'idle' || friends.status === 'loading'
);

/** Ids with a request in flight, so one row's spinner is not every row's. */
const busy = ref<string[]>([]);

function isBusy(userId: string): boolean {
  return busy.value.includes(userId);
}

async function withBusy(userId: string, work: () => Promise<unknown>): Promise<void> {
  if (isBusy(userId)) return;
  busy.value = [...busy.value, userId];
  try {
    await work();
  } finally {
    busy.value = busy.value.filter((id) => id !== userId);
  }
}

// ------------------------------------------------------------------- search

const searching = computed(() => renderedQuery.value.length > 0);

/**
 * The store's answer when it has one, the search response otherwise. The store
 * only reports a *positive* relationship, so an unloaded store cannot make
 * someone look like a stranger.
 */
function relationshipFor(result: UserSearchResult): UserSearchResult['relationship'] {
  return friends.relationshipOf(result.id) ?? result.relationship;
}

async function addFriend(result: UserSearchResult): Promise<void> {
  await withBusy(result.id, async () => {
    const outcome = await friends.sendRequest(result.id);

    if (outcome === 'sent') {
      toasts.show(`Request sent to ${result.displayName}.`);
      return;
    }

    if (outcome === 'already_friends') {
      toasts.show(`You are already friends with ${result.displayName}.`);
      return;
    }

    if (outcome === 'request_pending') {
      // The reload has settled which direction it was. The useful half of this
      // message is the one where they asked first.
      toasts.show(
        friends.incomingRequestFrom(result.id)
          ? `${result.displayName} already sent you a request — accept it below.`
          : `Your request to ${result.displayName} is still pending.`
      );
    }
  });
}

/** Accepting from a search row means accepting the request they already sent. */
async function acceptFrom(result: UserSearchResult): Promise<void> {
  await withBusy(result.id, async () => {
    if (friends.status !== 'ready') await friends.load(true);

    const request = friends.incomingRequestFrom(result.id);
    if (!request) {
      // It was answered or withdrawn somewhere else; the reload above is the
      // correction and the button will have changed with it.
      toasts.show('That request is no longer waiting.');
      return;
    }

    await friends.accept(request.id);
  });
}

// ----------------------------------------------------------------- requests

async function accept(requestId: string): Promise<void> {
  await withBusy(requestId, () => friends.accept(requestId));
}

async function decline(requestId: string): Promise<void> {
  await withBusy(requestId, () => friends.decline(requestId));
}

/** The sender's own verb — withdrawing, not declining (FR-F1). */
async function cancel(requestId: string): Promise<void> {
  await withBusy(requestId, () => friends.cancel(requestId));
}

// ------------------------------------------------------------------- remove

/**
 * FR-F3 behind an overflow button and a confirm — never a bare "Remove" beside
 * a name. One sheet for the screen, not one per row.
 */
const removalTarget = ref<Friend | null>(null);
const removalOpen = ref(false);

const removalTitle = computed(() =>
  removalTarget.value ? `Remove ${removalTarget.value.user.displayName}?` : 'Remove friend'
);

function askToRemove(friend: Friend): void {
  removalTarget.value = friend;
  removalOpen.value = true;
}

async function confirmRemove(): Promise<void> {
  const target = removalTarget.value;
  removalOpen.value = false;
  if (!target) return;

  await withBusy(target.user.id, async () => {
    if (await friends.remove(target.user.id)) {
      toasts.show(`${target.user.displayName} is no longer on your friends list.`);
    }
  });
}
</script>

<template>
  <div>
    <ScreenHeader title="Friends" />

    <!-- ---------------------------------------------------------- search -->

    <div class="find">
      <label class="sr-only" for="user-search">Find people by display name</label>
      <span class="find__icon" aria-hidden="true"><IconSearch /></span>
      <input
        id="user-search"
        v-model="query"
        class="find__input"
        type="search"
        enterkeyhint="search"
        autocomplete="off"
        autocapitalize="none"
        spellcheck="false"
        placeholder="Display name"
        @keydown.enter.prevent="runNow"
      />
    </div>

    <section v-if="searching" class="section" aria-labelledby="friends-results">
      <h2 id="friends-results" class="section__title">Results</h2>

      <SpinnerBlock v-if="searchStatus === 'loading' && results.length === 0" label="Searching" />

      <ErrorState v-else-if="searchStatus === 'error'" :error="searchError" @retry="runNow" />

      <p v-else-if="results.length === 0" class="section__note">
        Nobody here goes by “{{ renderedQuery }}”. Display names are matched from the start.
      </p>

      <ul v-else class="people">
        <PersonRow v-for="result in results" :key="result.id" :user="result">
          <template #actions>
            <BaseButton
              v-if="relationshipFor(result) === 'none'"
              variant="primary"
              :loading="isBusy(result.id)"
              @click="addFriend(result)"
            >
              Add friend
            </BaseButton>

            <BaseButton
              v-else-if="relationshipFor(result) === 'request_received'"
              variant="primary"
              :loading="isBusy(result.id)"
              @click="acceptFrom(result)"
            >
              Accept
            </BaseButton>

            <BaseButton v-else-if="relationshipFor(result) === 'request_sent'" disabled>
              Pending
            </BaseButton>

            <BaseButton v-else disabled>Friends</BaseButton>
          </template>
        </PersonRow>
      </ul>
    </section>

    <!-- ------------------------------------------------------- the screen -->

    <SpinnerBlock v-if="loadingFriends" label="Loading your friends" />

    <ErrorState
      v-else-if="friends.status === 'error'"
      :error="friends.error"
      @retry="friends.load(true)"
    />

    <template v-else>
      <!-- 1. Requests to you — first, because they are the ones needing action. -->
      <section
        v-if="friends.incoming.length > 0"
        class="section"
        aria-labelledby="friends-incoming"
      >
        <h2 id="friends-incoming" class="section__title">Requests to you</h2>
        <ul class="people">
          <PersonRow
            v-for="request in friends.incoming"
            :key="request.id"
            :user="request.user"
          >
            <template #actions>
              <BaseButton
                variant="primary"
                :loading="isBusy(request.id)"
                @click="accept(request.id)"
              >
                Accept
              </BaseButton>
              <BaseButton
                variant="ghost"
                :disabled="isBusy(request.id)"
                @click="decline(request.id)"
              >
                Decline
              </BaseButton>
            </template>
          </PersonRow>
        </ul>
      </section>

      <!-- 2. Your friends. -->
      <section class="section" aria-labelledby="friends-list">
        <h2 id="friends-list" class="section__title">Your friends</h2>

        <EmptyState
          v-if="friends.friends.length === 0"
          headline="No friends yet"
          body="Search for someone by display name to send them a request."
        >
          <template #icon><IconPeople /></template>
        </EmptyState>

        <ul v-else class="people">
          <PersonRow
            v-for="friend in friends.friends"
            :key="friend.user.id"
            :user="friend.user"
            :to="`/u/${friend.user.id}`"
          >
            <template #meta>
              <TasteMatch :match="friend.tasteMatch" size="sm" />
            </template>
            <template #actions>
              <button
                type="button"
                class="overflow"
                :aria-label="`More options for ${friend.user.displayName}`"
                :disabled="isBusy(friend.user.id)"
                @click="askToRemove(friend)"
              >
                <IconMore />
              </button>
            </template>
          </PersonRow>
        </ul>
      </section>

      <!-- 3. Sent requests. -->
      <section
        v-if="friends.outgoing.length > 0"
        class="section"
        aria-labelledby="friends-outgoing"
      >
        <h2 id="friends-outgoing" class="section__title">Sent requests</h2>
        <ul class="people">
          <PersonRow
            v-for="request in friends.outgoing"
            :key="request.id"
            :user="request.user"
          >
            <template #actions>
              <span class="pending">Pending</span>
              <BaseButton
                variant="ghost"
                :loading="isBusy(request.id)"
                :aria-label="`Withdraw your request to ${request.user.displayName}`"
                @click="cancel(request.id)"
              >
                Withdraw
              </BaseButton>
            </template>
          </PersonRow>
        </ul>
      </section>
    </template>

    <!-- FR-F3: destructive, so it asks first and never sits beside the name. -->
    <BaseSheet v-model:open="removalOpen" :title="removalTitle">
      <p class="sheet-copy">
        You will stop seeing each other's activity, lists, and ratings. You can send a new
        request later.
      </p>
      <template #actions>
        <BaseButton variant="ghost" @click="removalOpen = false">Cancel</BaseButton>
        <BaseButton variant="primary" @click="confirmRemove">Remove</BaseButton>
      </template>
    </BaseSheet>
  </div>
</template>

<style scoped>
.find {
  position: relative;
  display: flex;
  align-items: center;
  margin: 0 var(--space-4) var(--space-2);
}

.find__icon {
  position: absolute;
  left: var(--space-3);
  color: var(--text-muted);
  pointer-events: none;
}

.find__icon :deep(svg) {
  width: 18px;
  height: 18px;
}

.find__input {
  width: 100%;
  min-height: var(--tap-min);
  padding: 0 var(--space-3) 0 var(--space-8);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--surface);
  font-size: var(--text-base);
}

.section {
  padding: var(--space-4) var(--space-4) 0;
}

.section__title {
  font-size: var(--text-xs);
  letter-spacing: 0.16em;
  text-transform: uppercase;
  color: var(--text-muted);
  font-weight: 600;
  margin-bottom: var(--space-2);
}

.section__note {
  font-size: var(--text-sm);
  color: var(--text-muted);
}

.people {
  display: flex;
  flex-direction: column;
}

.overflow {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--tap-min);
  height: var(--tap-min);
  flex: none;
  border: 0;
  background: none;
  border-radius: var(--radius-full);
  color: var(--text-muted);
}

.overflow:disabled {
  opacity: 0.55;
}

.pending {
  font-size: var(--text-xs);
  color: var(--text-muted);
  padding: 0 var(--space-2);
}

.sheet-copy {
  font-size: var(--text-sm);
  color: var(--text-muted);
}

@media (min-width: 900px) {
  .find {
    margin: 0 var(--space-6) var(--space-2);
    max-width: 420px;
  }

  .section {
    padding: var(--space-4) var(--space-6) 0;
    max-width: 560px;
  }
}
</style>
