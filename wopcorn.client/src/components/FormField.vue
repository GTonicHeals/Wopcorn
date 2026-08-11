<script setup lang="ts">
import { computed, useId } from 'vue';

/**
 * A labelled control. Placeholder-only labelling is not accessible labelling
 * (NFR-8), so the <label> is always rendered and always visible.
 */
const props = withDefaults(
  defineProps<{
    label: string;
    type?: 'text' | 'email' | 'password';
    autocomplete?: string;
    required?: boolean;
    disabled?: boolean;
    maxlength?: number;
    hint?: string;
    /** Server-supplied messages for this field. */
    errors?: string[];
  }>(),
  {
    type: 'text',
    autocomplete: undefined,
    required: false,
    disabled: false,
    maxlength: undefined,
    hint: undefined,
    errors: () => []
  }
);

const model = defineModel<string>({ required: true });

const uid = useId();
const inputId = `field-${uid}`;
const errorId = `field-${uid}-error`;
const hintId = `field-${uid}-hint`;

const hasErrors = computed(() => props.errors.length > 0);
const describedBy = computed(() => {
  const ids = [];
  if (props.hint) ids.push(hintId);
  if (hasErrors.value) ids.push(errorId);
  return ids.length > 0 ? ids.join(' ') : undefined;
});
</script>

<template>
  <div class="field">
    <label class="field__label" :for="inputId">{{ label }}</label>
    <input
      :id="inputId"
      v-model="model"
      class="field__input"
      :class="{ 'field__input--invalid': hasErrors }"
      :type="type"
      :autocomplete="autocomplete"
      :required="required"
      :disabled="disabled"
      :maxlength="maxlength"
      :aria-invalid="hasErrors || undefined"
      :aria-describedby="describedBy"
    />
    <p v-if="hint" :id="hintId" class="field__hint">{{ hint }}</p>
    <ul v-if="hasErrors" :id="errorId" class="field__errors">
      <li v-for="message in errors" :key="message">{{ message }}</li>
    </ul>
  </div>
</template>

<style scoped>
.field {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
}

.field__label {
  font-size: var(--text-xs);
  font-weight: 600;
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--text-muted);
}

.field__input {
  min-height: var(--tap-min);
  width: 100%;
  padding: 0 var(--space-3);
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  color: var(--text);
  /* 16px, not the body size — see base.css: below it iOS zooms in on focus. */
  font-size: 16px;
}

.field__input:disabled {
  opacity: 0.6;
}

/*
 * The palette has no danger colour and the accent is reserved for the user's own
 * state, so an invalid field is marked by weight rather than hue — and the
 * message below it is the real signal (NFR-9: never colour alone).
 */
.field__input--invalid {
  border-color: var(--text);
  box-shadow: inset 0 0 0 1px var(--text);
}

.field__hint {
  font-size: var(--text-xs);
  color: var(--text-muted);
}

.field__errors {
  display: flex;
  flex-direction: column;
  gap: 2px;
  font-size: var(--text-xs);
  font-weight: 600;
  color: var(--text);
}
</style>
