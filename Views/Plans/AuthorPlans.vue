<template>
    <div class="p-4">
        <h2 class="text-2xl font-bold mb-4">Author Plans</h2>

        <!-- Search box -->
        <input v-model="search"
               type="text"
               placeholder="Search by plan name..."
               class="border rounded p-2 w-full mb-3" />

        <!-- Plans table -->
        <table class="table-auto w-full border">
            <thead>
                <tr class="bg-gray-200">
                    <th class="px-4 py-2">Plan Name</th>
                    <th class="px-4 py-2">Start Date</th>
                    <th class="px-4 py-2">End Date</th>
                    <th class="px-4 py-2">Status</th>
                    <th class="px-4 py-2">Action</th>
                </tr>
            </thead>
            <tbody>
                <tr v-for="plan in paginatedPlans"
                    :key="plan.authorPlanId"
                    class="border-t">
                    <td class="px-4 py-2">{{ plan.planName }}</td>
                    <td class="px-4 py-2">{{ formatDate(plan.startDate) }}</td>
                    <td class="px-4 py-2">{{ formatDate(plan.endDate) }}</td>
                    <td class="px-4 py-2">
                        <span :class="plan.isActive ? 'text-green-600' : 'text-red-600'">
                            {{ plan.isActive ? "Active" : "Inactive" }}
                        </span>
                    </td>
                    <td class="px-4 py-2">
                        <button v-if="plan.isActive"
                                @click="cancelPlan(plan.authorPlanId)"
                                class="bg-red-500 text-white px-3 py-1 rounded">
                            Cancel
                        </button>
                        <button v-else
                                @click="assignPlan(plan.planId)"
                                class="bg-blue-500 text-white px-3 py-1 rounded">
                            Assign
                        </button>
                    </td>
                </tr>
            </tbody>
        </table>

        <!-- Pagination -->
        <div class="mt-3 flex justify-between">
            <button :disabled="page === 1"
                    @click="page--"
                    class="px-3 py-1 border rounded">
                Prev
            </button>
            <span>Page {{ page }} of {{ totalPages }}</span>
            <button :disabled="page === totalPages"
                    @click="page++"
                    class="px-3 py-1 border rounded">
                Next
            </button>
        </div>
    </div>
</template>

<script>
import axios from "axios";

export default {
  props: {
    authorId: {
      type: Number,
      required: true,
    },
  },
  data() {
    return {
      plans: [],
      search: "",
      page: 1,
      perPage: 5,
    };
  },
  computed: {
    filteredPlans() {
      return this.plans.filter((p) =>
        p.planName.toLowerCase().includes(this.search.toLowerCase())
      );
    },
    totalPages() {
      return Math.ceil(this.filteredPlans.length / this.perPage);
    },
    paginatedPlans() {
      const start = (this.page - 1) * this.perPage;
      return this.filteredPlans.slice(start, start + this.perPage);
    },
  },
  methods: {
    async fetchPlans() {
      const res = await axios.get(`/api/AuthorPlans/by-author/${this.authorId}`);
      this.plans = res.data;
    },
    async cancelPlan(planId) {
      await axios.post(`/api/AuthorPlans/cancel/${planId}`, "Cancelled by user", {
        headers: { "Content-Type": "application/json" },
      });
      this.fetchPlans();
    },
    async assignPlan(planId) {
      const newPlan = {
        authorId: this.authorId,
        planId: planId,
        startDate: new Date().toISOString(),
        endDate: new Date(new Date().setMonth(new Date().getMonth() + 1)),
        isActive: true,
      };
      await axios.post(`/api/AuthorPlans/assign`, newPlan);
      this.fetchPlans();
    },
    formatDate(date) {
      return new Date(date).toLocaleDateString();
    },
  },
  mounted() {
    this.fetchPlans();
  },
};
</script>

