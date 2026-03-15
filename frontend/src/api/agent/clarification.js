/**
 * Agent Clarification API
 * Handles clarification responses for Human-in-the-Loop feature
 */
import { useMutation } from '@tanstack/react-query';
import axiosInstance from '../axios';
import { API_ENDPOINTS } from '../../constants';

/**
 * Send clarification answer to the backend
 * POST /api/agent/clarify/{sessionId}
 * 
 * @param {string} sessionId - The session ID from the clarification request
 * @param {Object} answer - The answer object
 * @param {string} [answer.answer] - Free text answer
 * @param {string} [answer.selectedOption] - Selected option from available options
 * @param {boolean} [answer.confirmed] - For DML confirmation (true/false)
 * @returns {Promise<Object>} - Response from the server
 */
const sendClarificationAnswer = async ({ sessionId, answer }) => {
  const response = await axiosInstance.post(
    `${API_ENDPOINTS.AGENT}/clarify/${sessionId}`,
    answer
  );
  return response.data;
};

/**
 * useClarificationAnswerMutation - React Query mutation hook for sending clarification answers
 * 
 * @param {Object} options - Mutation options
 * @param {Function} options.onSuccess - Callback on success
 * @param {Function} options.onError - Callback on error
 */
export const useClarificationAnswerMutation = ({ onSuccess, onError } = {}) => {
  return useMutation({
    mutationFn: sendClarificationAnswer,
    onSuccess: (data, variables, context) => {
      if (onSuccess) {
        onSuccess(data, variables, context);
      }
    },
    onError: (error, variables, context) => {
      if (onError) {
        onError(error, variables, context);
      }
    },
  });
};

export default {
  useClarificationAnswerMutation,
};
